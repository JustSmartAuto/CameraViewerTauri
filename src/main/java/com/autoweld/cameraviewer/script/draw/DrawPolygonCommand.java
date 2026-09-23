package com.autoweld.cameraviewer.script.draw;

import java.awt.*;
import java.awt.geom.AffineTransform;
import java.awt.geom.Point2D;

public class DrawPolygonCommand implements DrawingCommand {
    private final double[] points;
    private final float thickness;
    private final Color color;

    public DrawPolygonCommand(double[] points, float thickness, Color color) {
        this.points = points;
        this.thickness = thickness;
        this.color = color;
    }

    @Override
    public void draw(Graphics2D g2d, AffineTransform imageToScreen) {
        if (points == null || points.length < 4 || points.length % 2 != 0) {
            return;
        }
        int n = points.length / 2;
        int[] xs = new int[n];
        int[] ys = new int[n];
        for (int i = 0; i < n; i++) {
            Point2D p = CoordinateMapper.mapPoint(imageToScreen, points[i * 2], points[i * 2 + 1]);
            xs[i] = (int) Math.round(p.getX());
            ys[i] = (int) Math.round(p.getY());
        }
        float width = Math.max(1.0f, (float) CoordinateMapper.mapLength(imageToScreen, thickness));
        g2d.setColor(color);
        g2d.setStroke(new BasicStroke(width));
        g2d.drawPolygon(xs, ys, n);
    }
}
