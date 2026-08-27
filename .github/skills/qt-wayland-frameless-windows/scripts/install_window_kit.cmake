cmake_minimum_required(VERSION 3.16)

if(NOT DEFINED DESTINATION OR DESTINATION STREQUAL "")
    message(FATAL_ERROR
        "DESTINATION is required. Example: "
        "cmake -DDESTINATION=/workspace/src/qt-wayland-window-kit "
        "-P install_window_kit.cmake")
endif()

get_filename_component(_source
    "${CMAKE_CURRENT_LIST_DIR}/../assets/qt-wayland-window-kit"
    ABSOLUTE)
get_filename_component(_destination
    "${DESTINATION}"
    ABSOLUTE
    BASE_DIR "${CMAKE_CURRENT_BINARY_DIR}")

if(NOT IS_DIRECTORY "${_source}")
    message(FATAL_ERROR "Bundled window kit was not found: ${_source}")
endif()

if(EXISTS "${_destination}")
    file(GLOB _existing_entries LIST_DIRECTORIES true
        "${_destination}/*"
        "${_destination}/.[!.]*"
        "${_destination}/..?*")
    if(_existing_entries)
        message(FATAL_ERROR
            "Destination is not empty; refusing to overwrite: ${_destination}")
    endif()
endif()

file(MAKE_DIRECTORY "${_destination}")
file(COPY "${_source}/" DESTINATION "${_destination}")

message(STATUS "Installed Qt Wayland window kit to: ${_destination}")
