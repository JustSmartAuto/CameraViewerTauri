package com.autoweld.cameraviewer.script.draw;

import java.awt.*;
import java.awt.geom.AffineTransform;
import java.awt.geom.Point2D;

/**
 * 坐标与颜色工具。
 */
public final class CoordinateMapper {
    private CoordinateMapper() {
    }

    public static double getScale(AffineTransform transform) {
        return Math.sqrt(Math.abs(transform.getDeterminant()));
    }

    public static Point2D mapPoint(AffineTransform transform, double imageX, double imageY) {
        return transform.transform(new Point2D.Double(imageX, imageY), null);
    }

    public static double mapLength(AffineTransform transform, double imageLength) {
        return imageLength * getScale(transform);
    }

    public static Color parseColor(Object colorObj) {
        if (colorObj instanceof Number) {
            int rgb = ((Number) colorObj).intValue();
            return new Color(rgb);
        }
        if (colorObj instanceof String) {
            String s = ((String) colorObj).trim();
            if (s.startsWith("#")) {
                s = s.substring(1);
            }
            try {
                return new Color(Integer.parseUnsignedInt(s, 16));
            } catch (NumberFormatException e) {
                return Color.WHITE;
            }
        }
        return Color.WHITE;
    }
}
