package com.autoweld.cameraviewer.script.draw;

import java.awt.*;
import java.awt.geom.AffineTransform;

/**
 * 脚本绘制的抽象指令。
 */
public interface DrawingCommand {
    void draw(Graphics2D g2d, AffineTransform imageToScreen);
}
