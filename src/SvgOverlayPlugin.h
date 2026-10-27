/*******************************************************************************************************
 SvgOverlayPlugin.h

 nomacs plugin: when enabled, composites the current image with a same-name
 SVG annotation file (e.g. foo.svg next to foo.bmp) and replaces the displayed
 image via DkImageContainer::setImage(). Since nomacs itself then manages all
 zoom/pan/aspect ratio, the annotations stay perfectly registered regardless
 of viewport state. Disabling calls DkImageContainer::undo() to restore the
 original image.

 Supported image extensions: bmp, png, avif, jpg, jpeg
 *******************************************************************************************************/

#pragma once

#include <QAction>
#include <QImage>
#include <QPointer>
#include <QSvgRenderer>
#include <QTimer>

#include "DkPluginInterface.h"

namespace nso
{

// plugin interface
class SvgOverlayPlugin : public QObject, public nmc::DkPluginInterface
{
    Q_OBJECT
    Q_INTERFACES(nmc::DkPluginInterface)
    Q_PLUGIN_METADATA(IID "com.nomacs.ImageLounge.DkPluginInterface/3.6" FILE "SvgOverlayPlugin.json")

public:
    SvgOverlayPlugin(QObject *parent = nullptr);
    ~SvgOverlayPlugin() override;

    QImage image() const override;
    QList<QAction *> createActions(QWidget *parent) override;
    QList<QAction *> pluginActions() const override;
    QSharedPointer<nmc::DkImageContainer> runPlugin(const QString &runID,
                                                    QSharedPointer<nmc::DkImageContainer> imgC) const override;

private slots:
    void toggleOverlay();
    void saveComposite();    // save the composited image next to the original as *_叠加.jpeg
    void pollImage();        // watches for file changes while active
    void autoApply();        // retry applying the persisted state until nomacs is ready

private:
    QWidget *findViewport(QWidget *widget) const;
    QString  svgPathFor(const QString &imagePath) const;
    bool     compositeAndSet();                       // renders SVG on top of the current image
    void     setActiveInternal(bool on);               // real work behind toggle
    void     saveState();
    bool     loadState();
    QSharedPointer<nmc::DkImageContainerT> currentContainer() const; // locate via DkViewPort

    QTimer  mPollTimer;
    QTimer  mAutoApplyTimer;                // waits for nomacs startup
    QImage  mOriginalImage;                 // saved on toggle on, restored on toggle off
    QString mLastImagePath;                 // used to detect file changes in pollImage()
    QString mLastSvgPath;
    QSharedPointer<nmc::DkImageContainerT> mCurrentImgC;  // the container we composite into
    bool    mPersistedActive = false;       // last saved active flag, auto-applied after load
    QAction *mAction = nullptr;
    QAction *mSaveAction = nullptr;
    bool    mActive = false;
    static const QStringList mSupportedExts;
};

}
