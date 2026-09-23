package com.autoweld.cameraviewer.script.draw;

import java.awt.*;
import java.awt.geom.AffineTransform;
import java.awt.geom.Ellipse2D;
import java.awt.geom.Point2D;

public class DrawPointCommand implements DrawingCommand {
    private final double x;
    private final double y;
    private final float thickness;
    private final Color color;

    public DrawPointCommand(double x, double y, float thickness, Color color) {
        this.x = x;
        this.y = y;
        this.thickness = thickness;
        this.color = color;
    }

    @Override
    public void draw(Graphics2D g2d, AffineTransform imageToScreen) {
        Point2D p = CoordinateMapper.mapPoint(imageToScreen, x, y);
        double size = Math.max(2.0, CoordinateMapper.mapLength(imageToScreen, thickness));
        g2d.setColor(color);
        g2d.fill(new Ellipse2D.Double(p.getX() - size / 2, p.getY() - size / 2, size, size));
    }
}
