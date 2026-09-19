#include <cassert>
#include "display/st7796_config.hpp"

int main(){
 using namespace streamdeck::display::st7796;
 static_assert(logical_width==480&&logical_height==320);
 static_assert(spi_frequency_hz==10'000'000&&landscape_madctl==0x28);
 static_assert(rgb565_pixel_format==0x55&&transfer_pixels==128);
 static_assert(cooperative_rows==1);
 static_assert(frame_bytes==307200);
 static_assert(spi_wire_time_us(frame_bytes)==245760);
 static_assert(spi_wire_time_us(transfer_pixels*2)==205);
 static_assert(chip_selects_exclusive(true,true));
 static_assert(chip_selects_exclusive(false,true));
 static_assert(chip_selects_exclusive(true,false));
 static_assert(!chip_selects_exclusive(false,false));
 static_assert(transfer_count(480)==4);
 static_assert(transfer_count(128)==1&&transfer_count(129)==2);
 TransferCursor chunks(std::size_t(logical_width)*logical_height);
 TransferChunk chunk{};std::size_t expected_offset=0,chunk_count=0;
 while(chunks.next(chunk)){
  assert(chunk.offset==expected_offset);
  assert(chunk.count>0&&chunk.count<=transfer_pixels);
  expected_offset+=chunk.count;++chunk_count;
 }
 assert(expected_offset==std::size_t(logical_width)*logical_height);
 assert(chunk_count==1200);
 assert(!chunks.next(chunk));
 TransferCursor row_chunks(logical_width);
 expected_offset=0;chunk_count=0;
 while(row_chunks.next(chunk)){
  AddressWindow resumed{};
  assert(make_address_window(static_cast<uint16_t>(chunk.offset),42,
                             static_cast<uint16_t>(chunk.count),1,resumed));
  assert(resumed.x0==chunk.offset&&resumed.x1==chunk.offset+chunk.count-1);
  assert(resumed.y0==42&&resumed.y1==42);
  assert(chunk.offset==expected_offset);
  expected_offset+=chunk.count;++chunk_count;
 }
 assert(expected_offset==logical_width&&chunk_count==4);
 static_assert(red==0xF800&&green==0x07E0&&blue==0x001F);
 static_assert(white==0xFFFF&&black==0x0000);
 AddressWindow window{};
 assert(make_address_window(0,0,480,320,window));
 assert(window.x0==0&&window.y0==0&&window.x1==479&&window.y1==319);
 assert(make_address_window(479,319,1,1,window));
 assert(window.x0==479&&window.y0==319&&window.x1==479&&window.y1==319);
 assert(!make_address_window(0,0,0,1,window));
 assert(!make_address_window(0,0,1,0,window));
 assert(!make_address_window(480,0,1,1,window));
 assert(!make_address_window(0,320,1,1,window));
 assert(!make_address_window(470,0,11,1,window));
 assert(!make_address_window(0,310,1,11,window));
}
