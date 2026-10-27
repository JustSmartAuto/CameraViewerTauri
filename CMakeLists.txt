cmake_minimum_required(VERSION 3.16)

project(SvgOverlayPlugin VERSION 1.0.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_AUTOMOC ON)
set(CMAKE_AUTORCC ON)
set(CMAKE_AUTOUIC ON)

# --- nomacs ---------------------------------------------------------------
# Point NOMACS_ROOT to a nomacs source/build tree, e.g.:
#   cmake -B build -DNOMACS_ROOT=C:/nomacs
set(NOMACS_ROOT "" CACHE PATH "Path to the nomacs source tree (or install prefix)")

find_package(Qt6 COMPONENTS Widgets Svg QUIET)
if (NOT Qt6_FOUND)
    find_package(Qt5 5.15 REQUIRED COMPONENTS Widgets Svg)
    set(QT_VERSION_MAJOR 5)
else ()
    set(QT_VERSION_MAJOR 6)
endif ()

# nomacs headers: look for DkPluginInterface.h
find_path(NOMACS_INCLUDE_DIR
    NAMES DkPluginInterface.h
    PATHS
        ${NOMACS_ROOT}/ImageLounge/src/DkCore
        ${NOMACS_ROOT}/include/nomacs
        ${NOMACS_ROOT}/include
        ${CMAKE_PREFIX_PATH}
)

if (NOT NOMACS_INCLUDE_DIR)
    message(FATAL_ERROR "DkPluginInterface.h not found – set NOMACS_ROOT to a nomacs source tree or install prefix")
endif ()

message(STATUS "nomacs headers: ${NOMACS_INCLUDE_DIR}")

add_library(SvgOverlayPlugin SHARED
    src/SvgOverlayPlugin.h
    src/SvgOverlayPlugin.cpp
    src/SvgOverlayPlugin.json
)

target_include_directories(SvgOverlayPlugin PRIVATE
    ${NOMACS_INCLUDE_DIR}
    # DkPluginInterface.h pulls in a few sibling headers
    ${NOMACS_INCLUDE_DIR}/..
)

target_link_libraries(SvgOverlayPlugin PRIVATE
    Qt${QT_VERSION_MAJOR}::Widgets
    Qt${QT_VERSION_MAJOR}::Svg
)

# nomacs plugins on Windows live in <nomacs>/plugins or the plugin search path
if (WIN32)
    set_target_properties(SvgOverlayPlugin PROPERTIES
        RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/SvgOverlayPlugin"
    )
endif ()

# install next to nomacs plugins
install(TARGETS SvgOverlayPlugin
    RUNTIME DESTINATION plugins/SvgOverlayPlugin
    LIBRARY DESTINATION plugins/SvgOverlayPlugin
)
