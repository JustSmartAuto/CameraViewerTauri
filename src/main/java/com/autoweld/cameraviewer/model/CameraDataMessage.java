package com.autoweld.cameraviewer.model;

import java.util.Objects;

/**
 * 测量数据消息，对应 MQTT 话题 /autoweld/measure/data/raw。
 */
public class CameraDataMessage {
    private String timestamp;
    private int imageHeight;
    private int imageWidth;
    private int productNumber;
    private int surfaceNumber;

    // 底座
    private double baseLineLeftPointX;
    private double baseLineLeftPointY;
    private double baseLineRightPointX;
    private double baseLineRightPointY;

    // 宽度
    private double leftPointX;
    private double leftPointY;
    private double rightPointX;
    private double rightPointY;
    private double productWidth;

    // 高度
    private double topPointX;
    private double topPointY;
    private double productHeight;

    // 对称度
    private double symmetry;

    public CameraDataMessage() {
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

    public double getBaseLineLeftPointX() {
        return baseLineLeftPointX;
    }

    public void setBaseLineLeftPointX(double baseLineLeftPointX) {
        this.baseLineLeftPointX = baseLineLeftPointX;
    }

    public double getBaseLineLeftPointY() {
        return baseLineLeftPointY;
    }

    public void setBaseLineLeftPointY(double baseLineLeftPointY) {
        this.baseLineLeftPointY = baseLineLeftPointY;
    }

    public double getBaseLineRightPointX() {
        return baseLineRightPointX;
    }

    public void setBaseLineRightPointX(double baseLineRightPointX) {
        this.baseLineRightPointX = baseLineRightPointX;
    }

    public double getBaseLineRightPointY() {
        return baseLineRightPointY;
    }

    public void setBaseLineRightPointY(double baseLineRightPointY) {
        this.baseLineRightPointY = baseLineRightPointY;
    }

    public double getLeftPointX() {
        return leftPointX;
    }

    public void setLeftPointX(double leftPointX) {
        this.leftPointX = leftPointX;
    }

    public double getLeftPointY() {
        return leftPointY;
    }

    public void setLeftPointY(double leftPointY) {
        this.leftPointY = leftPointY;
    }

    public double getRightPointX() {
        return rightPointX;
    }

    public void setRightPointX(double rightPointX) {
        this.rightPointX = rightPointX;
    }

    public double getRightPointY() {
        return rightPointY;
    }

    public void setRightPointY(double rightPointY) {
        this.rightPointY = rightPointY;
    }

    public double getProductWidth() {
        return productWidth;
    }

    public void setProductWidth(double productWidth) {
        this.productWidth = productWidth;
    }

    public double getTopPointX() {
        return topPointX;
    }

    public void setTopPointX(double topPointX) {
        this.topPointX = topPointX;
    }

    public double getTopPointY() {
        return topPointY;
    }

    public void setTopPointY(double topPointY) {
        this.topPointY = topPointY;
    }

    public double getProductHeight() {
        return productHeight;
    }

    public void setProductHeight(double productHeight) {
        this.productHeight = productHeight;
    }

    public double getSymmetry() {
        return symmetry;
    }

    public void setSymmetry(double symmetry) {
        this.symmetry = symmetry;
    }

    @Override
    public boolean equals(Object o) {
        if (this == o) return true;
        if (!(o instanceof CameraDataMessage)) return false;
        CameraDataMessage that = (CameraDataMessage) o;
        return imageHeight == that.imageHeight &&
                imageWidth == that.imageWidth &&
                productNumber == that.productNumber &&
                surfaceNumber == that.surfaceNumber &&
                Double.compare(that.baseLineLeftPointX, baseLineLeftPointX) == 0 &&
                Double.compare(that.baseLineLeftPointY, baseLineLeftPointY) == 0 &&
                Double.compare(that.baseLineRightPointX, baseLineRightPointX) == 0 &&
                Double.compare(that.baseLineRightPointY, baseLineRightPointY) == 0 &&
                Double.compare(that.leftPointX, leftPointX) == 0 &&
                Double.compare(that.leftPointY, leftPointY) == 0 &&
                Double.compare(that.rightPointX, rightPointX) == 0 &&
                Double.compare(that.rightPointY, rightPointY) == 0 &&
                Double.compare(that.productWidth, productWidth) == 0 &&
                Double.compare(that.topPointX, topPointX) == 0 &&
                Double.compare(that.topPointY, topPointY) == 0 &&
                Double.compare(that.productHeight, productHeight) == 0 &&
                Double.compare(that.symmetry, symmetry) == 0 &&
                Objects.equals(timestamp, that.timestamp);
    }

    @Override
    public int hashCode() {
        return Objects.hash(timestamp, imageHeight, imageWidth, productNumber, surfaceNumber,
                baseLineLeftPointX, baseLineLeftPointY, baseLineRightPointX, baseLineRightPointY,
                leftPointX, leftPointY, rightPointX, rightPointY, productWidth,
                topPointX, topPointY, productHeight, symmetry);
    }

    @Override
    public String toString() {
        return "CameraDataMessage{" +
                "timestamp='" + timestamp + '\'' +
                ", imageHeight=" + imageHeight +
                ", imageWidth=" + imageWidth +
                ", productNumber=" + productNumber +
                ", surfaceNumber=" + surfaceNumber +
                ", productWidth=" + productWidth +
                ", productHeight=" + productHeight +
                ", symmetry=" + symmetry +
                '}';
    }
}
