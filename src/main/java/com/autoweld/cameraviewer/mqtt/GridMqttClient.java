package com.autoweld.cameraviewer.mqtt;

import com.autoweld.cameraviewer.model.GridConfig;
import org.eclipse.paho.client.mqttv3.*;
import org.eclipse.paho.client.mqttv3.persist.MemoryPersistence;

import java.util.logging.Level;
import java.util.logging.Logger;

/**
 * 单个画面格子对应的 MQTT 客户端封装。
 */
public class GridMqttClient implements MqttCallback {
    private static final Logger LOGGER = Logger.getLogger(GridMqttClient.class.getName());
    private static final int CONNECTION_TIMEOUT = 5;
    private static final int KEEP_ALIVE = 15;
    private static final int QOS = 1;

    private final int gridIndex;
    private final MqttMessageListener listener;
    private final MemoryPersistence persistence;

    private MqttClient client;
    private GridConfig config;
    private boolean connected;

    public GridMqttClient(int gridIndex, MqttMessageListener listener) {
        this.gridIndex = gridIndex;
        this.listener = listener;
        this.persistence = new MemoryPersistence();
        this.connected = false;
    }

    public synchronized void updateConfig(GridConfig config) {
        this.config = config;
        disconnect();
        if (config != null && config.isEnabled()) {
            connect();
        }
    }

    public synchronized void connect() {
        if (config == null || !config.isEnabled()) {
            return;
        }
        if (client != null && client.isConnected()) {
            return;
        }

        String brokerUrl = "tcp://" + config.getBrokerHost() + ":" + config.getBrokerPort();
        String clientId = config.getEffectiveClientId(gridIndex);

        try {
            client = new MqttClient(brokerUrl, clientId, persistence);
            MqttConnectOptions options = new MqttConnectOptions();
            options.setAutomaticReconnect(true);
            options.setCleanSession(true);
            options.setConnectionTimeout(CONNECTION_TIMEOUT);
            options.setKeepAliveInterval(KEEP_ALIVE);

            client.setCallback(this);
            client.connect(options);

            subscribe(config.getImageTopic(), MqttMessageListener.MessageType.IMAGE);
            subscribe(config.getDataTopic(), MqttMessageListener.MessageType.DATA);
            subscribe(config.getLogTopic(), MqttMessageListener.MessageType.LOG);

            connected = true;
            notifyConnectionChanged(true, "已连接");
            LOGGER.info("Grid " + gridIndex + " connected to " + brokerUrl);
        } catch (MqttException e) {
            connected = false;
            notifyConnectionChanged(false, e.getMessage());
            LOGGER.log(Level.WARNING, "Grid " + gridIndex + " failed to connect to " + brokerUrl, e);
        }
    }

    public synchronized void disconnect() {
        if (client != null) {
            try {
                if (client.isConnected()) {
                    client.disconnect();
                }
                client.close();
            } catch (MqttException e) {
                LOGGER.log(Level.WARNING, "Grid " + gridIndex + " disconnect error", e);
            } finally {
                client = null;
                connected = false;
                notifyConnectionChanged(false, "已断开");
            }
        }
    }

    public synchronized boolean isConnected() {
        return connected;
    }

    private void subscribe(String topic, MqttMessageListener.MessageType type) throws MqttException {
        if (topic == null || topic.trim().isEmpty()) {
            return;
        }
        String fullTopic = topic.startsWith("/") ? topic : "/" + topic;
        client.subscribe(fullTopic, QOS, (t, msg) -> {
            String payload = new String(msg.getPayload(), java.nio.charset.StandardCharsets.UTF_8);
            listener.onMessage(gridIndex, type, payload);
        });
    }

    private void notifyConnectionChanged(boolean connected, String reason) {
        if (listener != null) {
            listener.onConnectionChanged(gridIndex, connected, reason);
        }
    }

    @Override
    public void connectionLost(Throwable cause) {
        connected = false;
        notifyConnectionChanged(false, cause != null ? cause.getMessage() : "连接丢失");
        LOGGER.log(Level.WARNING, "Grid " + gridIndex + " connection lost", cause);
    }

    @Override
    public void messageArrived(String topic, MqttMessage message) {
        // 使用 lambda 订阅，此处不处理
    }

    @Override
    public void deliveryComplete(IMqttDeliveryToken token) {
        // 本应用只订阅不发布
    }
}
