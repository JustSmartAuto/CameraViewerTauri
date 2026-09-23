package com.autoweld.cameraviewer.config;

import com.autoweld.cameraviewer.model.GridConfig;

import java.util.ArrayList;
import java.util.List;
import java.util.Objects;

/**
 * 全局应用配置，包含 4 个画面格子的配置。
 */
public class AppConfig {
    public static final int GRID_COUNT = 4;
    public static final String CURRENT_VERSION = "1.0.0";

    private String version;
    private boolean maximizeOnStartup;
    private boolean showCheckerboardWhenNoImage;
    private List<GridConfig> grids;

    public AppConfig() {
        this.version = CURRENT_VERSION;
        this.maximizeOnStartup = false;
        this.showCheckerboardWhenNoImage = false;
        this.grids = new ArrayList<>();
        for (int i = 0; i < GRID_COUNT; i++) {
            GridConfig grid = new GridConfig();
            grid.setName("画面 " + (i + 1));
            grids.add(grid);
        }
    }

    public String getVersion() {
        return version;
    }

    public void setVersion(String version) {
        this.version = version;
    }

    public boolean isMaximizeOnStartup() {
        return maximizeOnStartup;
    }

    public void setMaximizeOnStartup(boolean maximizeOnStartup) {
        this.maximizeOnStartup = maximizeOnStartup;
    }

    public boolean isShowCheckerboardWhenNoImage() {
        return showCheckerboardWhenNoImage;
    }

    public void setShowCheckerboardWhenNoImage(boolean showCheckerboardWhenNoImage) {
        this.showCheckerboardWhenNoImage = showCheckerboardWhenNoImage;
    }

    public List<GridConfig> getGrids() {
        return grids;
    }

    public void setGrids(List<GridConfig> grids) {
        this.grids = grids;
    }

    public GridConfig getGrid(int index) {
        if (index < 0 || index >= grids.size()) {
            throw new IndexOutOfBoundsException("Invalid grid index: " + index);
        }
        return grids.get(index);
    }

    public void setGrid(int index, GridConfig grid) {
        if (index < 0 || index >= grids.size()) {
            throw new IndexOutOfBoundsException("Invalid grid index: " + index);
        }
        grids.set(index, grid);
    }

    @Override
    public boolean equals(Object o) {
        if (this == o) return true;
        if (!(o instanceof AppConfig)) return false;
        AppConfig appConfig = (AppConfig) o;
        return maximizeOnStartup == appConfig.maximizeOnStartup &&
                showCheckerboardWhenNoImage == appConfig.showCheckerboardWhenNoImage &&
                Objects.equals(version, appConfig.version) &&
                Objects.equals(grids, appConfig.grids);
    }

    @Override
    public int hashCode() {
        return Objects.hash(version, maximizeOnStartup, showCheckerboardWhenNoImage, grids);
    }
}
