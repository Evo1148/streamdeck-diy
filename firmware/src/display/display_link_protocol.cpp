#include "display/display_link_protocol.hpp"
namespace streamdeck::display {
uint16_t read_u16(const uint8_t* p){ return uint16_t(p[0]) | uint16_t(uint16_t(p[1])<<8); }
int16_t read_i16(const uint8_t* p){ return static_cast<int16_t>(read_u16(p)); }
uint32_t read_u32(const uint8_t* p){ return uint32_t(p[0]) | (uint32_t(p[1])<<8) | (uint32_t(p[2])<<16) | (uint32_t(p[3])<<24); }
void write_u16(uint8_t* p,uint16_t v){p[0]=uint8_t(v);p[1]=uint8_t(v>>8);}
void write_i16(uint8_t* p,int16_t v){write_u16(p,static_cast<uint16_t>(v));}
void write_u32(uint8_t* p,uint32_t v){p[0]=uint8_t(v);p[1]=uint8_t(v>>8);p[2]=uint8_t(v>>16);p[3]=uint8_t(v>>24);}
bool decode_envelope(const uint8_t* d,std::size_t n,Envelope& e){ if(!d||n<DISPLAY_ENVELOPE_SIZE)return false;e={d[0],d[1],d[2],d[3],read_u32(d+4)};return true; }
void encode_envelope(uint8_t* d,const Envelope& e){d[0]=e.major;d[1]=e.minor;d[2]=e.opcode;d[3]=e.flags;write_u32(d+4,e.generation);}
}
