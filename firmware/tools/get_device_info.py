import struct
import sys

try:
    import hid
except ImportError:
    sys.exit("Falta hidapi. Instálalo dentro de un entorno virtual con: python -m pip install hidapi")

VID = 0xCAFE
PID = 0x4004
USAGE_PAGE_VENDOR = 0xFF00
PACKET_SIZE = 64
MAGIC = 0x4453
PROTOCOL_VERSION = 1
GET_DEVICE_INFO = 0x01
DEVICE_INFO = 0x81


def main():
    interfaces = hid.enumerate(VID, PID)
    controls = [item for item in interfaces if item.get("usage_page") == USAGE_PAGE_VENDOR]
    if not controls:
        sys.exit("No se encontró la interfaz HID StreamDeck Control.")

    packet = bytearray(PACKET_SIZE)
    struct.pack_into("<HBBHH", packet, 0, MAGIC, PROTOCOL_VERSION,
                     GET_DEVICE_INFO, 1, 0)

    device = hid.device()
    device.open_path(controls[0]["path"])
    try:
        if device.write(bytes([0]) + packet) <= 0:
            sys.exit("No se pudo enviar GET_DEVICE_INFO.")
        response = bytes(device.read(PACKET_SIZE, 3000))
    finally:
        device.close()

    if len(response) != PACKET_SIZE:
        sys.exit("No se recibió una respuesta completa de 64 bytes.")

    magic, version, message_type, sequence, payload_length = struct.unpack_from(
        "<HBBHH", response, 0)
    if magic != MAGIC or version != PROTOCOL_VERSION or message_type != DEVICE_INFO:
        sys.exit("La respuesta no es un DEVICE_INFO válido.")
    if sequence != 1 or payload_length != 8:
        sys.exit("DEVICE_INFO contiene una cabecera inesperada.")

    protocol_version, major, minor, patch, buttons, encoders, capabilities = (
        struct.unpack_from("<BBBBBBH", response, 8)
    )
    print(f"Protocol: {protocol_version}")
    print(f"Firmware: {major}.{minor}.{patch}")
    print(f"Buttons: {buttons}")
    print(f"Encoders: {encoders}")
    print(f"Capabilities: 0x{capabilities:04X}")


if __name__ == "__main__":
    main()
