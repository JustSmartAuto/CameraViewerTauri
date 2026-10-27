#include "SvgOverlayPlugin.h"

#include <QCoreApplication>
#include <QDebug>
#include <QDir>
#include <QFile>
#include <QFileInfo>
#include <QMainWindow>
#include <QMessageBox>
#include <QMetaObject>
#include <QPainter>
#include <QSettings>
#include <QSvgRenderer>

// --- nomacsCore ABI bridge ---------------------------------------------------
// nomacs does not install DkViewPort.h / DkBasicLoader.h, so we redeclare only
// what we actually call. The signatures are verified at link time against the
// auto-generated nomacsCore import library (see build-zig.ps1).
namespace nmc
{
class DkBasicLoader
{
public:
    __declspec(dllimport) void pruneEditHistory();
    __declspec(dllimport) bool isDirty() const;
};

class DkViewPort
{
public:
    __declspec(dllimport) QSharedPointer<DkImageContainerT> imageContainer() const;
};
}

namespace nso
{

const QStringList SvgOverlayPlugin::mSupportedExts = {"bmp", "png", "avif", "jpg", "jpeg", "svg"};

// ---------------------------------------------------------------- helpers

QString SvgOverlayPlugin::svgPathFor(const QString &imagePath) const
{
    QFileInfo fi(imagePath);

    if (!fi.exists() || !mSupportedExts.contains(fi.suffix(), Qt::CaseInsensitive))
        return QString();

    // opening the svg itself (e.g. double-click) also renders it onto a blank canvas
    if (fi.suffix().compare("svg", Qt::CaseInsensitive) == 0)
        return fi.absoluteFilePath();

    return fi.dir().absoluteFilePath(fi.completeBaseName() + ".svg");
}

QWidget *SvgOverlayPlugin::findViewport(QWidget *widget) const
{
    if (!widget)
        return nullptr;

    if (QString(widget->metaObject()->className()).startsWith("nmc::DkViewPort"))
        return widget;

    const QObjectList children = widget->children();
    for (QObject *c : children) {
        if (QWidget *w = qobject_cast<QWidget *>(c)) {
            if (QWidget *vp = findViewport(w))
                return vp;
        }
    }
    return nullptr;
}

// replace the displayed image of the current container with the SVG-composited version.
// returns true on success (container still has a valid image after the call).
bool SvgOverlayPlugin::compositeAndSet()
{
    if (!mCurrentImgC || mOriginalImage.isNull())
        return false;

    const QString imagePath = mCurrentImgC->filePath();
    const QString svgPath   = svgPathFor(imagePath);

    // SVG as the primary source (user double-clicked foo.svg) – render onto a blank image
    QImage canvas = (imagePath.endsWith(".svg", Qt::CaseInsensitive))
                        ? QImage(mOriginalImage.size(), QImage::Format_ARGB32_Premultiplied)
                        : mOriginalImage.copy();
    if (imagePath.endsWith(".svg", Qt::CaseInsensitive))
        canvas.fill(Qt::black);

    // nomacs loads some formats (e.g. 8-bit BMP) as Indexed8 / BGR888 which
    // QPainter cannot paint into – force a paint-friendly pixel format first.
    if (canvas.format() != QImage::Format_ARGB32_Premultiplied) {
        canvas = canvas.convertToFormat(QImage::Format_ARGB32_Premultiplied);
    }

    if (svgPath.isEmpty() || !QFileInfo(svgPath).exists()) {
        return false;
    }

    QPainter p(&canvas);
    p.setRenderHint(QPainter::Antialiasing);
    p.setRenderHint(QPainter::SmoothPixmapTransform);

    // Strip the <image xlink:href="...bmp"> element – Tiny QtSvg often gives
    // up on the whole document when an external image ref can't be resolved,
    // and we already have the bmp as canvas background.
    QFile svgFile(svgPath);
    if (!svgFile.open(QIODevice::ReadOnly)) {
        qWarning() << "[SvgOverlay] cannot open" << svgPath;
        return false;
    }
    QByteArray svgData = svgFile.readAll();
    svgFile.close();
    QString stripped = QString::fromUtf8(svgData);
    int imgStart;
    while ((imgStart = stripped.indexOf("<image")) >= 0) {
        int imgEnd = stripped.indexOf("/>", imgStart);
        if (imgEnd < 0) break;
        stripped.remove(imgStart, imgEnd - imgStart + 2);
    }

    QSvgRenderer svgRenderer(stripped.toUtf8());
    if (!svgRenderer.isValid()) {
        qWarning() << "[SvgOverlay] svg invalid after stripping <image>";
        return false;
    }
    svgRenderer.render(&p);

    p.end();

    mCurrentImgC->setImage(canvas, "SVG Overlay");

    // the composite is a visual enhancement, not a real edit – don't ask the user
    // "save changes?" when they exit nomacs. setEdited(false) clears the dirty flag
    // and pruneEditHistory() removes our entry from the undo/redo stack so Ctrl+Z
    // behaves naturally after toggle-off / image navigation.
    mCurrentImgC->setEdited(false);
    mCurrentImgC->getLoader()->pruneEditHistory();

    QMetaObject::invokeMethod(mCurrentImgC.data(), "imageUpdatedSignal", Qt::QueuedConnection);

    return true;
}

// ---------------------------------------------------------------- plugin

// QSettings scope – append to nomacs own settings.ini so both apps share one file.
// Note: the path is hard-coded because QSettings(orgName, appName, IniFormat) would
// produce "nomacs/Image Lounge.ini" but nomacs itself uses "nomacs/Image Lounge/settings.ini".
static QString settingsIniPath()
{
    const QString roaming = qEnvironmentVariable("APPDATA", "C:/Users/Public/AppData/Roaming");
    return roaming + "/nomacs/Image Lounge/settings.ini";
}

static QString s_settingKey = "SvgOverlayPlugin/active";

SvgOverlayPlugin::SvgOverlayPlugin(QObject *parent)
    : QObject(parent)
{
    connect(&mPollTimer,      &QTimer::timeout, this, &SvgOverlayPlugin::pollImage);
    connect(&mAutoApplyTimer, &QTimer::timeout, this, &SvgOverlayPlugin::autoApply);

    mPersistedActive = loadState();

    // auto-apply once nomacs has finished its startup sequence (main window + image ready).
    // We poll because the plugin itself is lazy-loaded – nomacs loads it only when the
    // plugin menu is first opened, which is *after* the main window exists but *before*
    // any actions from our side.
    if (mPersistedActive) {
        mAutoApplyTimer.start(300);
        qInfo() << "[SvgOverlay] persisted state = on, waiting for nomacs to settle…";
    }
}

SvgOverlayPlugin::~SvgOverlayPlugin()
{
    mAutoApplyTimer.stop();
    mPollTimer.stop();

    saveState();

    // best-effort restore the original image before we are unloaded
    if (mCurrentImgC && !mOriginalImage.isNull()) {
        mCurrentImgC->setImage(mOriginalImage, "");
        mCurrentImgC->setEdited(false);
        mCurrentImgC->getLoader()->pruneEditHistory();
        QMetaObject::invokeMethod(mCurrentImgC.data(), "imageUpdatedSignal", Qt::QueuedConnection);
    }
}

void SvgOverlayPlugin::saveState()
{
    QSettings s(settingsIniPath(), QSettings::IniFormat);
    s.setValue(s_settingKey, mActive);
    s.sync();
}

bool SvgOverlayPlugin::loadState()
{
    QSettings s(settingsIniPath(), QSettings::IniFormat);
    return s.value(s_settingKey, false).toBool();
}

QImage SvgOverlayPlugin::image() const { return QImage(); }

QList<QAction *> SvgOverlayPlugin::createActions(QWidget *parent)
{
    QList<QAction *> actions;

    if (mAction)
        return actions;

    mAction = new QAction(tr("Toggle SVG Overlay"), parent);
    mAction->setObjectName("SvgOverlayPlugin.toggle_svg_overlay");
    mAction->setCheckable(true);
    mAction->setStatusTip(tr("Composites the current image with a same-name SVG file"));
    connect(mAction, &QAction::triggered, this, &SvgOverlayPlugin::toggleOverlay);

    mSaveAction = new QAction(tr("Save Composited Image"), parent);
    mSaveAction->setObjectName("SvgOverlayPlugin.save_composited");
    mSaveAction->setStatusTip(tr("Save current viewport image next to original as *_叠加.jpeg"));
    connect(mSaveAction, &QAction::triggered, this, &SvgOverlayPlugin::saveComposite);

    actions << mAction << mSaveAction;
    return actions;
}

QList<QAction *> SvgOverlayPlugin::pluginActions() const
{
    // nomacs builds the plugin menu from this list (not from createActions' return)
    QList<QAction *> list;
    if (mAction)     list << mAction;
    if (mSaveAction) list << mSaveAction;
    return list;
}

QSharedPointer<nmc::DkImageContainer> SvgOverlayPlugin::runPlugin(
    const QString &, QSharedPointer<nmc::DkImageContainer> imgC) const
{
    return imgC;
}

// ---------------------------------------------------------------- core logic

void SvgOverlayPlugin::toggleOverlay()
{
    // user action overrides the persisted state (e.g. they manually toggle off)
    mPersistedActive = !mActive;
    setActiveInternal(mPersistedActive);
    saveState();
}

void SvgOverlayPlugin::setActiveInternal(bool on)
{
    mActive = on;
    if (mAction)
        mAction->setChecked(mActive);

    if (!mActive) {
        mAutoApplyTimer.stop();
        mPollTimer.stop();
        if (mCurrentImgC && !mOriginalImage.isNull()) {
            mCurrentImgC->setImage(mOriginalImage, "");
            mCurrentImgC->setEdited(false);  // restoring is not a "real edit" either
            mCurrentImgC->getLoader()->pruneEditHistory();
            QMetaObject::invokeMethod(mCurrentImgC.data(), "imageUpdatedSignal", Qt::QueuedConnection);
        }
        mCurrentImgC.reset();
        mOriginalImage = QImage();
        mLastImagePath.clear();
        mLastSvgPath.clear();
        return;
    }

    QMainWindow *win = getMainWindow();
    if (!win) {
        qWarning() << "[SvgOverlay] could not find nomacs main window";
        if (mAction) mAction->setChecked(false);
        return;
    }

    QWidget *vp = findViewport(win);
    if (!vp) {
        qWarning() << "[SvgOverlay] could not locate the nomacs viewport";
        if (mAction) mAction->setChecked(false);
        return;
    }

    const auto *dkViewPort = reinterpret_cast<const nmc::DkViewPort *>(vp);
    mCurrentImgC = dkViewPort->imageContainer();
    if (!mCurrentImgC) {
        qWarning() << "[SvgOverlay] viewport has no image container";
        if (mAction) mAction->setChecked(false);
        return;
    }

    mOriginalImage = mCurrentImgC->image();
    if (mOriginalImage.isNull()) {
        // nomacs still loading an image – keep polling via mAutoApplyTimer
        qInfo() << "[SvgOverlay] image still loading, will retry";
        mCurrentImgC.reset();
        mActive = false;
        if (mAction) mAction->setChecked(false);
        return;
    }

    mLastImagePath = mCurrentImgC->filePath();
    mLastSvgPath   = svgPathFor(mLastImagePath);

    compositeAndSet();

    mAutoApplyTimer.stop();
    mPollTimer.start(400);
}

void SvgOverlayPlugin::autoApply()
{
    if (!mPersistedActive) {
        mAutoApplyTimer.stop();
        return;
    }

    // try again – nomacs might still be opening the image at plugin load time
    setActiveInternal(true);
    if (mActive) {
        qInfo() << "[SvgOverlay] auto-applied to" << mLastImagePath;
        // done – pollImage takes over from here
    }
}

void SvgOverlayPlugin::pollImage()
{
    if (!mActive || !mCurrentImgC)
        return;

    QMainWindow *win = getMainWindow();
    if (!win)
        return;

    QWidget *vp = findViewport(win);
    if (!vp)
        return;

    const auto *dkViewPort = reinterpret_cast<const nmc::DkViewPort *>(vp);
    QSharedPointer<nmc::DkImageContainerT> imgC = dkViewPort->imageContainer();
    if (!imgC) {
        mPollTimer.stop();
        return;
    }

    const QString imagePath = imgC->filePath();
    const QString svgPath   = svgPathFor(imagePath);

    // image file changed (user navigated / next image) – reconnect
    if (imagePath != mLastImagePath) {
        // restore the previous image's original first
        if (!mOriginalImage.isNull()) {
            mCurrentImgC->setImage(mOriginalImage, "");
            mCurrentImgC->setEdited(false);
            mCurrentImgC->getLoader()->pruneEditHistory();
            QMetaObject::invokeMethod(mCurrentImgC.data(), "imageUpdatedSignal", Qt::QueuedConnection);
        }

        mCurrentImgC   = imgC;
        mOriginalImage = imgC->image();
        mLastImagePath = imagePath;
        mLastSvgPath   = svgPath;

        if (!mOriginalImage.isNull())
            compositeAndSet();
        else {
            // user opened something without an image yet – wait
        }
        return;
    }

    // SVG file changed (e.g. detection program rewrote it) – re-composite
    if (svgPath != mLastSvgPath) {
        // restore the saved original and re-composite (setImage is not cumulative
        // so we always start from mOriginalImage)
        mCurrentImgC->setImage(mOriginalImage, "");
        mCurrentImgC->setEdited(false);
        mCurrentImgC->getLoader()->pruneEditHistory();
        QMetaObject::invokeMethod(mCurrentImgC.data(), "imageUpdatedSignal", Qt::QueuedConnection);
        compositeAndSet();
        mLastSvgPath = svgPath;
    }
}

// find the currently displayed DkImageContainer via the viewport.
// mCurrentImgC may have been reset (e.g. user toggled overlay off), so we
// re-resolve from the nomacs main window every time the user clicks Save.
QSharedPointer<nmc::DkImageContainerT> SvgOverlayPlugin::currentContainer() const
{
    QMainWindow *win = getMainWindow();
    if (!win) return nullptr;

    QWidget *vp = findViewport(win);
    if (!vp) return nullptr;

    const auto *dkViewPort = reinterpret_cast<const nmc::DkViewPort *>(vp);
    return dkViewPort->imageContainer();
}

void SvgOverlayPlugin::saveComposite()
{
    auto imgC = currentContainer();
    if (!imgC) {
        QMessageBox::warning(nullptr, tr("Save composited image"),
                             tr("No image is currently displayed."));
        return;
    }

    QImage composite = imgC->image();
    if (composite.isNull()) {
        QMessageBox::warning(nullptr, tr("Save composited image"),
                             tr("Cannot read the current image."));
        return;
    }

    const QString srcPath  = imgC->filePath();
    const QFileInfo fi(srcPath);
    const QString outPath  = fi.dir().absoluteFilePath(fi.completeBaseName() + "_叠加.jpeg");

    // 匹配原图 DPI：优先用 toggle 时缓存的 mOriginalImage，
    // 否则临时读原图取 DPI（几行像素，只读元数据也很快）。
    double dpiX = mOriginalImage.dotsPerMeterX();
    double dpiY = mOriginalImage.dotsPerMeterY();
    if (dpiX <= 0 || dpiY <= 0) {
        QImage probe(srcPath);
        dpiX = probe.dotsPerMeterX();
        dpiY = probe.dotsPerMeterY();
    }
    if (dpiX > 0 && dpiY > 0) {
        composite.setDotsPerMeterX(qRound(dpiX));
        composite.setDotsPerMeterY(qRound(dpiY));
    }

    if (composite.save(outPath, "JPEG", 95)) {
        QMessageBox::information(nullptr, tr("Save composited image"),
                                 tr("Saved:\n%1").arg(QDir::toNativeSeparators(outPath)));
    } else {
        QMessageBox::critical(nullptr, tr("Save composited image"),
                              tr("Failed to write:\n%1").arg(QDir::toNativeSeparators(outPath)));
    }
}

}
