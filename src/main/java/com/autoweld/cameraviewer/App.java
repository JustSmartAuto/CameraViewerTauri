package com.autoweld.cameraviewer;

import com.autoweld.cameraviewer.config.AppConfig;
import com.autoweld.cameraviewer.config.ConfigManager;
import com.autoweld.cameraviewer.ui.MainFrame;
import com.formdev.flatlaf.FlatDarkLaf;

import javax.swing.*;
import java.util.logging.Level;
import java.util.logging.Logger;

public class App {
    private static final Logger LOGGER = Logger.getLogger(App.class.getName());

    public static void main(String[] args) {
        LOGGER.info("Camera Viewer AutoWeld starting...");

        ConfigManager configManager = new ConfigManager();
        AppConfig config = configManager.load();
        LOGGER.info("Loaded " + config.getGrids().size() + " grid configs");

        SwingUtilities.invokeLater(() -> {
            try {
                UIManager.setLookAndFeel(new FlatDarkLaf());
            } catch (Exception e) {
                LOGGER.log(Level.WARNING, "Failed to set FlatLaf, fallback to system L&F", e);
                try {
                    UIManager.setLookAndFeel(UIManager.getSystemLookAndFeelClassName());
                } catch (Exception ex) {
                    LOGGER.log(Level.SEVERE, "Failed to set system L&F", ex);
                }
            }

            MainFrame mainFrame = new MainFrame(config, configManager);
            mainFrame.setVisible(true);
        });
    }
}
