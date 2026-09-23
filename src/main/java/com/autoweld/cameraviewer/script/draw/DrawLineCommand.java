package com.autoweld.cameraviewer.script.draw;

import java.awt.*;
import java.awt.geom.AffineTransform;
import java.awt.geom.Line2D;
import java.awt.geom.Point2D;

public class DrawLineCommand implements DrawingCommand {
    private final double x1;
    private final double y1;
    private final double x2;
    private final double y2;
    private final float thickness;
    private final Color color;

    public DrawLineCommand(double x1, double y1, double x2, double y2,
                           float thickness, Color color) {
        this.x1 = x1;
        this.y1 = y1;
        this.x2 = x2;
        this.y2 = y2;
        this.thickness = thickness;
        this.color = color;
    }

    @Override
    public void draw(Graphics2D g2d, AffineTransform imageToScreen) {
        Point2D p1 = CoordinateMapper.mapPoint(imageToScreen, x1, y1);
        Point2D p2 = CoordinateMapper.mapPoint(imageToScreen, x2, y2);
        float width = Math.max(1.0f, (float) CoordinateMapper.mapLength(imageToScreen, thickness));
        g2d.setColor(color);
        g2d.setStroke(new BasicStroke(width));
        g2d.draw(new Line2D.Double(p1, p2));
    }
}
