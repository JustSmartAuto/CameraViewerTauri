package com.autoweld.cameraviewer.mqtt;

/**
 * MQTT 消息监听器，供 UI 层实现以接收解析后的消息。
 */
public interface MqttMessageListener {

    enum MessageType {
        IMAGE,
        DATA,
        LOG
    }

    /**
     * 当指定格子的 MQTT 消息到达时被调用。
     *
     * @param gridIndex 格子索引 0-3
     * @param type      消息类型
     * @param payload   原始消息字符串
     */
    void onMessage(int gridIndex, MessageType type, String payload);

    /**
     * 当连接状态变化时被调用。
     *
     * @param gridIndex 格子索引 0-3
     * @param connected 是否已连接
     * @param reason    状态变化原因
     */
    void onConnectionChanged(int gridIndex, boolean connected, String reason);
}
