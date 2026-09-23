package com.autoweld.cameraviewer.model;

import java.util.Objects;

/**
 * 日志消息，对应 MQTT 话题 /autoweld/measure/logs/raw。
 */
public class CameraLogMessage {
    private String timestamp;
    private int imageHeight;
    private int imageWidth;
    private int productNumber;
    private int surfaceNumber;
    private String message;

    public CameraLogMessage() {
    }

    public String getTimestamp() {
        return timestamp;
    }

    public void setTimestamp(String timestamp) {
        this.timestamp = timestamp;
    }

    public int getImageHeight() {
        return imageHeight;
    }

    public void setImageHeight(int imageHeight) {
        this.imageHeight = imageHeight;
    }

    public int getImageWidth() {
        return imageWidth;
    }

    public void setImageWidth(int imageWidth) {
        this.imageWidth = imageWidth;
    }

    public int getProductNumber() {
        return productNumber;
    }

    public void setProductNumber(int productNumber) {
        this.productNumber = productNumber;
    }

    public int getSurfaceNumber() {
        return surfaceNumber;
    }

    public void setSurfaceNumber(int surfaceNumber) {
        this.surfaceNumber = surfaceNumber;
    }

    public String getMessage() {
        return message;
    }

    public void setMessage(String message) {
        this.message = message;
    }

    @Override
    public boolean equals(Object o) {
        if (this == o) return true;
        if (!(o instanceof CameraLogMessage)) return false;
        CameraLogMessage that = (CameraLogMessage) o;
        return imageHeight == that.imageHeight &&
                imageWidth == that.imageWidth &&
                productNumber == that.productNumber &&
                surfaceNumber == that.surfaceNumber &&
                Objects.equals(timestamp, that.timestamp) &&
                Objects.equals(message, that.message);
    }

    @Override
    public int hashCode() {
        return Objects.hash(timestamp, imageHeight, imageWidth, productNumber, surfaceNumber, message);
    }

    @Override
    public String toString() {
        return "CameraLogMessage{" +
                "timestamp='" + timestamp + '\'' +
                ", imageHeight=" + imageHeight +
                ", imageWidth=" + imageWidth +
                ", productNumber=" + productNumber +
                ", surfaceNumber=" + surfaceNumber +
                ", message='" + message + '\'' +
                '}';
    }
}
