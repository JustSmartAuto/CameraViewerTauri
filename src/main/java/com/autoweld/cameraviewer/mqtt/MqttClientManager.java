package com.autoweld.cameraviewer.mqtt;

import com.autoweld.cameraviewer.config.AppConfig;
import com.autoweld.cameraviewer.model.GridConfig;

import java.util.ArrayList;
import java.util.List;
import java.util.logging.Logger;

/**
 * 管理 4 个格子的 MQTT 客户端生命周期。
 */
public class MqttClientManager {
    private static final Logger LOGGER = Logger.getLogger(MqttClientManager.class.getName());

    private final List<GridMqttClient> clients;

    public MqttClientManager(MqttMessageListener listener) {
        this.clients = new ArrayList<>(AppConfig.GRID_COUNT);
        for (int i = 0; i < AppConfig.GRID_COUNT; i++) {
            clients.add(new GridMqttClient(i, listener));
        }
    }

    /**
     * 应用配置变更后调用：停止所有旧连接并按新配置重建。
     */
    public void applyConfig(AppConfig config) {
        for (int i = 0; i < AppConfig.GRID_COUNT; i++) {
            GridConfig gridConfig = config.getGrid(i);
            clients.get(i).updateConfig(gridConfig);
        }
    }

    /**
     * 重新连接所有已启用格子。
     */
    public void reconnectAll() {
        for (GridMqttClient client : clients) {
            client.connect();
        }
    }

    /**
     * 断开所有格子连接。
     */
    public void disconnectAll() {
        for (GridMqttClient client : clients) {
            client.disconnect();
        }
    }

    public boolean isConnected(int gridIndex) {
        return clients.get(gridIndex).isConnected();
    }
}
