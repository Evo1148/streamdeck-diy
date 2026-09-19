#pragma once
#include <cstddef>
#include <cstdint>

namespace streamdeck::display {
inline constexpr uint8_t DISPLAY_LINK_MAJOR = 1;
inline constexpr uint8_t DISPLAY_LINK_MINOR = 0;
inline constexpr std::size_t DISPLAY_ENVELOPE_SIZE = 8;
inline constexpr std::size_t DISPLAY_BODY_MAX = 48;

enum class Opcode : uint8_t {
    GetInfo=0x01, GetStatus=0x02, BeginSync=0x03, CommitSync=0x04, CancelSync=0x05,
    BeginUpdate=0x06, CommitUpdate=0x07, DefineNode=0x10, PatchNode=0x11,
    DeleteNode=0x12, SetStringFragment=0x13, DefineTouchRegion=0x20,
    DeleteTouchRegion=0x21, DefineAnimation=0x30, DefineKeyframe=0x31,
    PlayAnimation=0x32, StopAnimation=0x33, DeleteAnimation=0x34,
    AssetBegin=0x40, AssetChunk=0x41, AssetCommit=0x42, AssetRelease=0x43,
    ClockSync=0x50, Heartbeat=0x51, TouchEvent=0x80, AnimationEvent=0x81,
};
enum class Error : uint8_t {
    None=0, UnsupportedVersion=1, UnsupportedOpcode=2, UnsupportedProperty=3,
    InvalidGeneration=4, InvalidNode=5, InvalidParent=6, InvalidRegion=7,
    OutOfBounds=8, UnsupportedPrimitive=9, UnsupportedIcon=10,
    UnsupportedFormat=11, InvalidString=12, AssetTooLarge=13, AssetPoolFull=14,
    TransferNotFound=15, CrcMismatch=16, NoMemory=17, Busy=18,
    TouchUnavailable=19, InvalidState=20, InvalidAnimation=21,
    TooManyNodes=22, TooManyRegions=23, TooManyAnimations=24,
};
enum Flags : uint8_t { AckRequired=0x01, ResponseError=0x80 };

struct Envelope { uint8_t major{}, minor{}, opcode{}, flags{}; uint32_t generation{}; };
uint16_t read_u16(const uint8_t* p);
int16_t read_i16(const uint8_t* p);
uint32_t read_u32(const uint8_t* p);
void write_u16(uint8_t* p, uint16_t v);
void write_i16(uint8_t* p, int16_t v);
void write_u32(uint8_t* p, uint32_t v);
bool decode_envelope(const uint8_t* data, std::size_t length, Envelope& envelope);
void encode_envelope(uint8_t* data, const Envelope& envelope);
}
