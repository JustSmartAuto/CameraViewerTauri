package com.autoweld.cameraviewer.model;

import java.util.Objects;

/**
 * 图像消息，对应 MQTT 话题 /autoweld/measure/image/raw。
 */
public class CameraImageMessage {
    private String timestamp;
    private int imageHeight;
    private int imageWidth;
    private int productNumber;
    private int surfaceNumber;
    private String imageData;

    public CameraImageMessage() {
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

    public String getImageData() {
        return imageData;
    }

    public void setImageData(String imageData) {
        this.imageData = imageData;
    }

    @Override
    public boolean equals(Object o) {
        if (this == o) return true;
        if (!(o instanceof CameraImageMessage)) return false;
        CameraImageMessage that = (CameraImageMessage) o;
        return imageHeight == that.imageHeight &&
                imageWidth == that.imageWidth &&
                productNumber == that.productNumber &&
                surfaceNumber == that.surfaceNumber &&
                Objects.equals(timestamp, that.timestamp) &&
                Objects.equals(imageData, that.imageData);
    }

    @Override
    public int hashCode() {
        return Objects.hash(timestamp, imageHeight, imageWidth, productNumber, surfaceNumber, imageData);
    }

    @Override
    public String toString() {
        return "CameraImageMessage{" +
                "timestamp='" + timestamp + '\'' +
                ", imageHeight=" + imageHeight +
                ", imageWidth=" + imageWidth +
                ", productNumber=" + productNumber +
                ", surfaceNumber=" + surfaceNumber +
                ", imageDataLength=" + (imageData != null ? imageData.length() : 0) +
                '}';
    }
}
