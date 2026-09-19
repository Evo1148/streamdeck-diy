# Generated Cmake Pico project file

cmake_minimum_required(VERSION 3.13)

set(CMAKE_C_STANDARD 11)
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_EXPORT_COMPILE_COMMANDS ON)

# Initialise pico_sdk from installed location
# (note this can come from environment, CMake cache etc)

# == DO NOT EDIT THE FOLLOWING LINES for the Raspberry Pi Pico VS Code Extension to work ==
if(WIN32)
    set(USERHOME $ENV{USERPROFILE})
else()
    set(USERHOME $ENV{HOME})
endif()
set(sdkVersion 2.3.1)
set(toolchainVersion 15_2_Rel1)
set(picotoolVersion 2.3.1)
set(picoVscode ${USERHOME}/.pico-sdk/cmake/pico-vscode.cmake)
if (EXISTS ${picoVscode})
    include(${picoVscode})
endif()
# ====================================================================================
set(PICO_BOARD waveshare_rp2040_zero CACHE STRING "Board type")

# Pull in Raspberry Pi Pico SDK (must be before project)
include(pico_sdk_import.cmake)

project(StreamDeck_Firmware C CXX ASM)

# Initialise the Raspberry Pi Pico SDKS
pico_sdk_init()

if(NOT PICO_SDK_VERSION_STRING VERSION_EQUAL "2.3.1")
    message(FATAL_ERROR "StreamDeck requires Pico SDK 2.3.1")
endif()
if(NOT PICO_BOARD STREQUAL "waveshare_rp2040_zero")
    message(FATAL_ERROR "StreamDeck requires waveshare_rp2040_zero")
endif()

# Add executable. Default name is the project name, version 0.1

add_executable(StreamDeck_Firmware
    src/main.cpp
    src/actions/action_engine.cpp
    src/config/config_manager.cpp
    src/config/config_record.cpp
    src/config/config_storage.cpp
    src/display/display_assets.cpp
    src/display/display_backend.cpp
    src/display/display_link_protocol.cpp
    src/display/display_runtime.cpp
    src/display/display_scene.cpp
    src/display/display_rasterizer.cpp
    src/display/st7796_display.cpp
    src/hardware/status_led.cpp
    src/input/encoder.cpp
    src/input/encoder_logic.cpp
    src/input/matrix_debouncer.cpp
    src/input/matrix_scanner.cpp
    src/protocol/action_codec.cpp
    src/system/bootloader.cpp
    src/touch/touch_math.cpp
    src/touch/touch_calibration.cpp
    src/touch/xpt2046.cpp
    src/usb/usb_device.cpp
    src/usb/hid_report_scheduler.cpp
    src/usb/control_hid.cpp
    src/usb/usb_descriptors.c
    src/protocol/protocol.cpp
)
target_compile_options(StreamDeck_Firmware PRIVATE -Wall -Wextra)

pico_generate_pio_header(
    StreamDeck_Firmware
    ${CMAKE_CURRENT_LIST_DIR}/src/hardware/ws2812.pio
)

pico_set_program_name(StreamDeck_Firmware "StreamDeck_Firmware")
pico_set_program_version(StreamDeck_Firmware "0.1")

# Modify the below lines to enable/disable output over UART/USB
pico_enable_stdio_uart(StreamDeck_Firmware 0)
pico_enable_stdio_usb(StreamDeck_Firmware 0)

# Add the standard library to the build
target_link_libraries(StreamDeck_Firmware
    pico_stdlib
    hardware_pio
    hardware_clocks
    pico_unique_id
    pico_rand
    pico_flash
    hardware_flash
    hardware_spi
    tinyusb_device
)

# Keep the board's physical 2 MiB size while limiting only the firmware linker region.
pico_add_linker_script_override_path(
    StreamDeck_Firmware
    ${CMAKE_CURRENT_LIST_DIR}/linker
    FILES memory_flash.incl
)

# Add the standard include files to the build
target_include_directories(StreamDeck_Firmware PRIVATE
        ${CMAKE_CURRENT_LIST_DIR}/src
        ${CMAKE_CURRENT_LIST_DIR}/src/usb
)

pico_add_extra_outputs(StreamDeck_Firmware)



# Matrix-only diagnostic firmware: no TFT, touch, encoder, storage or DisplayLink.
add_executable(StreamDeck_MatrixTest
    diagnostics/matrix_hid_test/main.cpp
    diagnostics/matrix_hid_test/usb_descriptors.c
    src/input/matrix_debouncer.cpp
)
target_compile_options(StreamDeck_MatrixTest PRIVATE -Wall -Wextra)
target_include_directories(StreamDeck_MatrixTest PRIVATE
    ${CMAKE_CURRENT_LIST_DIR}/diagnostics/matrix_hid_test
    ${CMAKE_CURRENT_LIST_DIR}/src
)
target_link_libraries(StreamDeck_MatrixTest
    pico_stdlib
    pico_unique_id
    tinyusb_device
)
pico_set_program_name(StreamDeck_MatrixTest "StreamDeck_MatrixTest")
pico_set_program_version(StreamDeck_MatrixTest "1.0")
pico_enable_stdio_uart(StreamDeck_MatrixTest 1)
pico_enable_stdio_usb(StreamDeck_MatrixTest 0)
pico_add_linker_script_override_path(
    StreamDeck_MatrixTest
    ${CMAKE_CURRENT_LIST_DIR}/linker
    FILES memory_flash.incl
)
pico_add_extra_outputs(StreamDeck_MatrixTest)

# Encoder-only diagnostic firmware: no matrix, TFT, touch, storage or DisplayLink.
add_executable(StreamDeck_EncoderTest
    diagnostics/encoder_hid_test/main.cpp
    diagnostics/encoder_hid_test/usb_descriptors.c
    src/input/encoder.cpp
    src/input/encoder_logic.cpp
)
target_compile_options(StreamDeck_EncoderTest PRIVATE -Wall -Wextra)
target_include_directories(StreamDeck_EncoderTest PRIVATE
    ${CMAKE_CURRENT_LIST_DIR}/diagnostics/encoder_hid_test
    ${CMAKE_CURRENT_LIST_DIR}/src
)
target_link_libraries(StreamDeck_EncoderTest
    pico_stdlib
    pico_unique_id
    tinyusb_device
)
pico_set_program_name(StreamDeck_EncoderTest "StreamDeck_EncoderTest")
pico_set_program_version(StreamDeck_EncoderTest "1.0")
pico_enable_stdio_uart(StreamDeck_EncoderTest 1)
pico_enable_stdio_usb(StreamDeck_EncoderTest 0)
pico_add_linker_script_override_path(
    StreamDeck_EncoderTest
    ${CMAKE_CURRENT_LIST_DIR}/linker
    FILES memory_flash.incl
)
pico_add_extra_outputs(StreamDeck_EncoderTest)
