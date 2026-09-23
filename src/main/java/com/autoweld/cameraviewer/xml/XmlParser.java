package com.autoweld.cameraviewer.xml;

import com.autoweld.cameraviewer.model.CameraDataMessage;
import com.autoweld.cameraviewer.model.CameraImageMessage;
import com.autoweld.cameraviewer.model.CameraLogMessage;
import org.w3c.dom.Document;
import org.w3c.dom.Element;
import org.w3c.dom.Node;
import org.w3c.dom.NodeList;
import org.xml.sax.InputSource;

import javax.xml.parsers.DocumentBuilder;
import javax.xml.parsers.DocumentBuilderFactory;
import java.io.StringReader;
import java.util.logging.Level;
import java.util.logging.Logger;

/**
 * 解析三类 XML 消息：图像、测量数据、日志。
 */
public class XmlParser {
    private static final Logger LOGGER = Logger.getLogger(XmlParser.class.getName());

    private final DocumentBuilderFactory factory;

    public XmlParser() {
        this.factory = DocumentBuilderFactory.newInstance();
        this.factory.setNamespaceAware(false);
        this.factory.setIgnoringComments(true);
    }

    public CameraImageMessage parseImage(String xml) {
        if (xml == null || xml.trim().isEmpty()) {
            return null;
        }
        try {
            Element root = parseRoot(xml);
            CameraImageMessage msg = new CameraImageMessage();
            msg.setTimestamp(text(root, "TimeStamp"));
            msg.setImageHeight(intValue(root, "ImageHeight"));
            msg.setImageWidth(intValue(root, "ImageWidth"));
            msg.setProductNumber(intValue(root, "ProductNumber"));
            msg.setSurfaceNumber(intValue(root, "SurfaceNumber"));
            msg.setImageData(text(root, "ImageData"));
            return msg;
        } catch (Exception e) {
            LOGGER.log(Level.WARNING, "Failed to parse image xml", e);
            return null;
        }
    }

    public CameraDataMessage parseData(String xml) {
        if (xml == null || xml.trim().isEmpty()) {
            return null;
        }
        try {
            Element root = parseRoot(xml);
            CameraDataMessage msg = new CameraDataMessage();
            msg.setTimestamp(text(root, "TimeStamp"));
            msg.setImageHeight(intValue(root, "ImageHeight"));
            msg.setImageWidth(intValue(root, "ImageWidth"));
            msg.setProductNumber(intValue(root, "ProductNumber"));
            msg.setSurfaceNumber(intValue(root, "SurfaceNumber"));

            msg.setBaseLineLeftPointX(doubleValue(root, "BaseLineLeftPointX"));
            msg.setBaseLineLeftPointY(doubleValue(root, "BaseLineLeftPointY"));
            msg.setBaseLineRightPointX(doubleValue(root, "BaseLineRightPointX"));
            msg.setBaseLineRightPointY(doubleValue(root, "BaseLineRightPointY"));

            msg.setLeftPointX(doubleValue(root, "LeftPointX"));
            msg.setLeftPointY(doubleValue(root, "LeftPointY"));
            msg.setRightPointX(doubleValue(root, "RightPointX"));
            msg.setRightPointY(doubleValue(root, "RightPointY"));
            msg.setProductWidth(doubleValue(root, "ProductWidth"));

            msg.setTopPointX(doubleValue(root, "TopPointX"));
            msg.setTopPointY(doubleValue(root, "TopPointY"));
            msg.setProductHeight(doubleValue(root, "ProductHeight"));

            msg.setSymmetry(doubleValue(root, "Symmetry"));
            return msg;
        } catch (Exception e) {
            LOGGER.log(Level.WARNING, "Failed to parse data xml", e);
            return null;
        }
    }

    public CameraLogMessage parseLog(String xml) {
        if (xml == null || xml.trim().isEmpty()) {
            return null;
        }
        try {
            Element root = parseRoot(xml);
            CameraLogMessage msg = new CameraLogMessage();
            msg.setTimestamp(text(root, "TimeStamp"));
            msg.setImageHeight(intValue(root, "ImageHeight"));
            msg.setImageWidth(intValue(root, "ImageWidth"));
            msg.setProductNumber(intValue(root, "ProductNumber"));
            msg.setSurfaceNumber(intValue(root, "SurfaceNumber"));
            msg.setMessage(text(root, "Message"));
            return msg;
        } catch (Exception e) {
            LOGGER.log(Level.WARNING, "Failed to parse log xml", e);
            return null;
        }
    }

    private Element parseRoot(String xml) throws Exception {
        DocumentBuilder builder = factory.newDocumentBuilder();
        try {
            Document doc = builder.parse(new InputSource(new StringReader(xml)));
            return doc.getDocumentElement();
        } catch (Exception e) {
            // 兼容无根元素的 XML：自动包裹一层 <root>
            // 先移除 XML 声明，避免出现在根元素内部导致解析失败
            String content = xml.replaceFirst("<\\?xml[^?]*\\?>", "").trim();
            String wrapped = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<root>\n" + content + "\n</root>";
            Document doc = builder.parse(new InputSource(new StringReader(wrapped)));
            return doc.getDocumentElement();
        }
    }

    private static String text(Element parent, String tagName) {
        NodeList list = parent.getElementsByTagName(tagName);
        if (list.getLength() == 0) {
            return "";
        }
        Node node = list.item(0);
        return node.getTextContent() != null ? node.getTextContent().trim() : "";
    }

    private static int intValue(Element parent, String tagName) {
        String value = text(parent, tagName);
        if (value.isEmpty()) {
            return 0;
        }
        try {
            return Integer.parseInt(value);
        } catch (NumberFormatException e) {
            return 0;
        }
    }

    private static double doubleValue(Element parent, String tagName) {
        String value = text(parent, tagName);
        if (value.isEmpty()) {
            return 0.0;
        }
        try {
            return Double.parseDouble(value);
        } catch (NumberFormatException e) {
            return 0.0;
        }
    }
}
