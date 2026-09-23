package com.autoweld.cameraviewer.script.draw;

import java.awt.*;
import java.awt.geom.AffineTransform;
import java.awt.geom.Point2D;

public class DrawStringCommand implements DrawingCommand {
    private final double x;
    private final double y;
    private final String text;
    private final int fontSize;
    private final Color color;

    public DrawStringCommand(double x, double y, String text, int fontSize, Color color) {
        this.x = x;
        this.y = y;
        this.text = text;
        this.fontSize = fontSize;
        this.color = color;
    }

    @Override
    public void draw(Graphics2D g2d, AffineTransform imageToScreen) {
        Point2D p = CoordinateMapper.mapPoint(imageToScreen, x, y);
        g2d.setColor(color);
        int size = Math.max(8, (int) Math.round(CoordinateMapper.mapLength(imageToScreen, fontSize)));
        g2d.setFont(new Font(Font.MONOSPACED, Font.PLAIN, size));
        g2d.drawString(text != null ? text : "", (int) p.getX(), (int) p.getY());
    }
}
