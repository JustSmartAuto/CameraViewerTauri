package com.autoweld.cameraviewer.model;

import java.util.Objects;

/**
 * 单个画面格子的配置。
 */
public class GridConfig {
    private String name;
    private boolean enabled;

    // MQTT
    private String brokerHost;
    private int brokerPort;
    private String clientId;
    private String imageTopic;
    private String dataTopic;
    private String logTopic;

    // Scripting
    private String script;

    // Display options
    private boolean showImage;
    private boolean showData;
    private boolean showLog;
    private boolean showDrawings;

    public GridConfig() {
        this.name = "未命名";
        this.enabled = true;
        this.brokerHost = "127.0.0.1";
        this.brokerPort = 8907;
        this.clientId = "";
        this.imageTopic = "/autoweld/measure/image/raw";
        this.dataTopic = "/autoweld/measure/data/raw";
        this.logTopic = "/autoweld/measure/logs/raw";
        this.script = defaultScript();
        this.showImage = true;
        this.showData = true;
        this.showLog = true;
        this.showDrawings = true;
    }

    private static String defaultScript() {
        return "function process(message) {\n" +
                "    // 在对应格子渲染图像、数据或日志\n" +
                "    if (message.imageData) {\n" +
                "        ShowImage(message.imageData)\n" +
                "    }\n" +
                "}\n";
    }

    public String getName() {
        return name;
    }

    public void setName(String name) {
        this.name = name;
    }

    public boolean isEnabled() {
        return enabled;
    }

    public void setEnabled(boolean enabled) {
        this.enabled = enabled;
    }

    public String getBrokerHost() {
        return brokerHost;
    }

    public void setBrokerHost(String brokerHost) {
        this.brokerHost = brokerHost;
    }

    public int getBrokerPort() {
        return brokerPort;
    }

    public void setBrokerPort(int brokerPort) {
        this.brokerPort = brokerPort;
    }

    public String getClientId() {
        return clientId;
    }

    public void setClientId(String clientId) {
        this.clientId = clientId;
    }

    public String getImageTopic() {
        return imageTopic;
    }

    public void setImageTopic(String imageTopic) {
        this.imageTopic = imageTopic;
    }

    public String getDataTopic() {
        return dataTopic;
    }

    public void setDataTopic(String dataTopic) {
        this.dataTopic = dataTopic;
    }

    public String getLogTopic() {
        return logTopic;
    }

    public void setLogTopic(String logTopic) {
        this.logTopic = logTopic;
    }

    public String getScript() {
        return script;
    }

    public void setScript(String script) {
        this.script = script;
    }

    public boolean isShowImage() {
        return showImage;
    }

    public void setShowImage(boolean showImage) {
        this.showImage = showImage;
    }

    public boolean isShowData() {
        return showData;
    }

    public void setShowData(boolean showData) {
        this.showData = showData;
    }

    public boolean isShowLog() {
        return showLog;
    }

    public void setShowLog(boolean showLog) {
        this.showLog = showLog;
    }

    public boolean isShowDrawings() {
        return showDrawings;
    }

    public void setShowDrawings(boolean showDrawings) {
        this.showDrawings = showDrawings;
    }

    /**
     * 如果 clientId 为空，则基于索引生成一个默认 clientId。
     */
    public String getEffectiveClientId(int gridIndex) {
        if (clientId == null || clientId.trim().isEmpty()) {
            return "camera-viewer-" + gridIndex + "-" + System.currentTimeMillis();
        }
        return clientId;
    }

    @Override
    public boolean equals(Object o) {
        if (this == o) return true;
        if (!(o instanceof GridConfig)) return false;
        GridConfig that = (GridConfig) o;
        return enabled == that.enabled &&
                brokerPort == that.brokerPort &&
                showImage == that.showImage &&
                showData == that.showData &&
                showLog == that.showLog &&
                showDrawings == that.showDrawings &&
                Objects.equals(name, that.name) &&
                Objects.equals(brokerHost, that.brokerHost) &&
                Objects.equals(clientId, that.clientId) &&
                Objects.equals(imageTopic, that.imageTopic) &&
                Objects.equals(dataTopic, that.dataTopic) &&
                Objects.equals(logTopic, that.logTopic) &&
                Objects.equals(script, that.script);
    }

    @Override
    public int hashCode() {
        return Objects.hash(name, enabled, brokerHost, brokerPort, clientId,
                imageTopic, dataTopic, logTopic, script, showImage, showData, showLog, showDrawings);
    }

    @Override
    public String toString() {
        return "GridConfig{" +
                "name='" + name + '\'' +
                ", enabled=" + enabled +
                ", brokerHost='" + brokerHost + '\'' +
                ", brokerPort=" + brokerPort +
                ", clientId='" + clientId + '\'' +
                ", imageTopic='" + imageTopic + '\'' +
                ", dataTopic='" + dataTopic + '\'' +
                ", logTopic='" + logTopic + '\'' +
                ", showImage=" + showImage +
                ", showData=" + showData +
                ", showLog=" + showLog +
                ", showDrawings=" + showDrawings +
                '}';
    }
}
