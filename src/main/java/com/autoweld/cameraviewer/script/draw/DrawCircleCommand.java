package com.autoweld.cameraviewer.script.draw;

import java.awt.*;
import java.awt.geom.AffineTransform;
import java.awt.geom.Ellipse2D;
import java.awt.geom.Point2D;

public class DrawCircleCommand implements DrawingCommand {
    private final double centerX;
    private final double centerY;
    private final double diameter;
    private final float thickness;
    private final Color color;

    public DrawCircleCommand(double centerX, double centerY, double diameter,
                             float thickness, Color color) {
        this.centerX = centerX;
        this.centerY = centerY;
        this.diameter = diameter;
        this.thickness = thickness;
        this.color = color;
    }

    @Override
    public void draw(Graphics2D g2d, AffineTransform imageToScreen) {
        Point2D p = CoordinateMapper.mapPoint(imageToScreen, centerX, centerY);
        double d = CoordinateMapper.mapLength(imageToScreen, diameter);
        float width = Math.max(1.0f, (float) CoordinateMapper.mapLength(imageToScreen, thickness));
        g2d.setColor(color);
        g2d.setStroke(new BasicStroke(width));
        g2d.draw(new Ellipse2D.Double(p.getX() - d / 2, p.getY() - d / 2, d, d));
    }
}
