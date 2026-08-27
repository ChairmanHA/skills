add_library(QtWaylandWindowKit STATIC
    draggablewidgetcontroller.cpp
    draggablewidgetcontroller.h
    waylandwindowcontroller.cpp
    waylandwindowcontroller.h
)

add_library(QtWaylandWindowKit::QtWaylandWindowKit ALIAS QtWaylandWindowKit)

target_compile_features(QtWaylandWindowKit PUBLIC cxx_std_17)

target_include_directories(QtWaylandWindowKit PUBLIC
    ${CMAKE_CURRENT_SOURCE_DIR}/..
)

target_link_libraries(QtWaylandWindowKit PUBLIC
    Qt${QT_VERSION_MAJOR}::Core
    Qt${QT_VERSION_MAJOR}::Gui
    Qt${QT_VERSION_MAJOR}::Widgets
)
