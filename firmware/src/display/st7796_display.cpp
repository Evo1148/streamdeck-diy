#include "display/st7796_display.hpp"
#include <algorithm>
#include "hardware/gpio.h"
#include "hardware/spi.h"
#include "hardware/hardware_map.hpp"
#include "pico/stdlib.h"
#include "pico/time.h"

namespace streamdeck::display {
namespace {
constexpr uint8_t SWRESET=0x01, SLPOUT=0x11, INVOFF=0x20, DISPON=0x29;
constexpr uint8_t CASET=0x2A, PASET=0x2B, RAMWR=0x2C, MADCTL=0x36, COLMOD=0x3A;
void output_high(uint pin){gpio_init(pin);gpio_set_dir(pin,GPIO_OUT);gpio_put(pin,1);}
}

bool St7796DisplayBackend::init(){
 output_high(hardware::tft_cs);output_high(hardware::tft_dc);output_high(hardware::tft_reset);
 gpio_init(hardware::backlight);gpio_set_dir(hardware::backlight,GPIO_OUT);gpio_put(hardware::backlight,0);
 spi_init(spi1,st7796::spi_frequency_hz);spi_set_format(spi1,8,SPI_CPOL_0,SPI_CPHA_0,SPI_MSB_FIRST);
 gpio_set_function(hardware::spi_sck,GPIO_FUNC_SPI);gpio_set_function(hardware::spi_mosi,GPIO_FUNC_SPI);
 hardware_reset();command(SWRESET);sleep_ms(150);
 const uint8_t u1[]={0xC3},u2[]={0x96};command(0xF0,u1,sizeof u1);command(0xF0,u2,sizeof u2);
 const uint8_t b0[]={0x80},b4[]={0x01},b6[]={0x80,0x02,0x3B};
 const uint8_t e8[]={0x40,0x8A,0x00,0x00,0x29,0x19,0xA5,0x33};
 const uint8_t c1[]={0x06},c2[]={0xA7},c5[]={0x18},b7[]={0xC6};
 const uint8_t e0[]={0xF0,0x09,0x0B,0x06,0x04,0x15,0x2F,0x54,0x42,0x3C,0x17,0x14,0x18,0x1B};
 const uint8_t e1[]={0xE0,0x09,0x0B,0x06,0x04,0x03,0x2B,0x43,0x42,0x3B,0x16,0x14,0x17,0x1B};
 command(0xB0,b0,sizeof b0);command(0xB4,b4,sizeof b4);command(0xB6,b6,sizeof b6);
 command(0xE8,e8,sizeof e8);command(0xC1,c1,sizeof c1);command(0xC2,c2,sizeof c2);
 command(0xC5,c5,sizeof c5);command(0xB7,b7,sizeof b7);command(0xE0,e0,sizeof e0);command(0xE1,e1,sizeof e1);
 const uint8_t l1[]={0x69},l2[]={0x3C};command(0xF0,l1,sizeof l1);command(0xF0,l2,sizeof l2);
 command(SLPOUT);sleep_ms(150);command(COLMOD,&st7796::rgb565_pixel_format,1);
 command(MADCTL,&st7796::landscape_madctl,1);command(INVOFF);command(DISPON);sleep_ms(50);
 initialized_=true;gpio_put(hardware::backlight,1);return true;
}

bool St7796DisplayBackend::set_address_window(uint16_t x,uint16_t y,uint16_t width,uint16_t height){
 st7796::AddressWindow w{};if(!initialized_||!st7796::make_address_window(x,y,width,height,w))return false;
 const uint8_t xs[]={uint8_t(w.x0>>8),uint8_t(w.x0),uint8_t(w.x1>>8),uint8_t(w.x1)};
 const uint8_t ys[]={uint8_t(w.y0>>8),uint8_t(w.y0),uint8_t(w.y1>>8),uint8_t(w.y1)};
 command(CASET,xs,sizeof xs);command(PASET,ys,sizeof ys);command(RAMWR);return true;
}

void St7796DisplayBackend::write_pixels(const uint16_t*pixels,std::size_t count){
 performance_.pixel_bytes+=count*2;
 st7796::TransferCursor chunks(count);st7796::TransferChunk chunk{};
 while(chunks.next(chunk)){const uint16_t*source=pixels+chunk.offset;
  for(std::size_t i=0;i<chunk.count;i++){transfer_buffer_[i*2]=uint8_t(source[i]>>8);transfer_buffer_[i*2+1]=uint8_t(source[i]);}
  data(transfer_buffer_.data(),chunk.count*2);}
}

void St7796DisplayBackend::fill(uint16_t color){
 if(!render_enabled_)return;
 if(!set_address_window(0,0,st7796::logical_width,st7796::logical_height))return;
 for(std::size_t i=0;i<st7796::transfer_pixels;i++){transfer_buffer_[i*2]=uint8_t(color>>8);transfer_buffer_[i*2+1]=uint8_t(color);}
 std::size_t remaining=std::size_t(st7796::logical_width)*st7796::logical_height;
 performance_.pixel_bytes+=remaining*2;
 while(remaining){const std::size_t batch=std::min(remaining,st7796::transfer_pixels);data(transfer_buffer_.data(),batch*2);remaining-=batch;}
}

void St7796DisplayBackend::fill_rectangle(uint16_t x,uint16_t y,uint16_t width,uint16_t height,uint16_t color){
 if(!render_enabled_)return;
 if(!set_address_window(x,y,width,height))return;
 for(std::size_t i=0;i<st7796::transfer_pixels;i++){transfer_buffer_[i*2]=uint8_t(color>>8);transfer_buffer_[i*2+1]=uint8_t(color);}
 std::size_t remaining=std::size_t(width)*height;
 performance_.pixel_bytes+=remaining*2;
 while(remaining){const std::size_t batch=std::min(remaining,st7796::transfer_pixels);data(transfer_buffer_.data(),batch*2);remaining-=batch;}
}

void St7796DisplayBackend::draw_crosshair(uint16_t x,uint16_t y,uint16_t color){
 constexpr uint16_t radius=9,thickness=3;
 const uint16_t left=x>radius?uint16_t(x-radius):0;
 const uint16_t top=y>radius?uint16_t(y-radius):0;
 const uint16_t right=std::min<uint16_t>(st7796::logical_width-1,uint16_t(x+radius));
 const uint16_t bottom=std::min<uint16_t>(st7796::logical_height-1,uint16_t(y+radius));
 const uint16_t horizontal_y=std::min<uint16_t>(st7796::logical_height-thickness,
     y>thickness/2?uint16_t(y-thickness/2):0);
 const uint16_t vertical_x=std::min<uint16_t>(st7796::logical_width-thickness,
     x>thickness/2?uint16_t(x-thickness/2):0);
 fill_rectangle(left,horizontal_y,uint16_t(right-left+1),thickness,color);
 fill_rectangle(vertical_x,top,thickness,uint16_t(bottom-top+1),color);
}

void St7796DisplayBackend::run_bringup_test(){
 const uint16_t colors[]={st7796::red,st7796::green,st7796::blue,st7796::white,st7796::black};
 for(uint16_t color:colors){fill(color);sleep_ms(1000);}
}

void St7796DisplayBackend::show_waiting_screen(){
 restore_waiting_region({0,0,int16_t(st7796::logical_width),int16_t(st7796::logical_height)});
}

void St7796DisplayBackend::restore_waiting_region(const Rect&requested){
 if(!initialized_||!render_enabled_)return;
 const Rect region=DisplayRasterizer::clip_to_screen(requested);if(region.width<=0||region.height<=0)return;
 for(int32_t y=region.y;y<region.y+region.height;y+=st7796::cooperative_rows){
  const int16_t h=int16_t(std::min<int32_t>(st7796::cooperative_rows,region.y+region.height-y));
  const Rect strip{region.x,int16_t(y),region.width,h};
  rasterizer_.render_waiting_strip(strip,render_strip_.data(),std::size_t(strip.width)*strip.height);
  for(std::size_t offset=0;offset<std::size_t(strip.width)*strip.height;offset+=st7796::transfer_pixels){
   const std::size_t count=std::min(st7796::transfer_pixels,std::size_t(strip.width)*strip.height-offset);
   if(!set_address_window(uint16_t(strip.x+offset),uint16_t(strip.y),uint16_t(count),1))return;
   write_pixels(render_strip_.data()+offset,count);service();}
 }
}

void St7796DisplayBackend::render_region(const Scene&scene,const AssetPool&assets,const Rect&requested){
 const Rect region=DisplayRasterizer::clip_to_screen(requested);if(region.width<=0||region.height<=0)return;
 for(int32_t y=region.y;y<region.y+region.height;y+=st7796::cooperative_rows){
  const int16_t h=int16_t(std::min<int32_t>(st7796::cooperative_rows,region.y+region.height-y));
  const Rect strip{region.x,int16_t(y),region.width,h};
  rasterizer_.render_strip(scene,&assets,strip,render_strip_.data(),std::size_t(strip.width)*strip.height);
  for(std::size_t offset=0;offset<std::size_t(strip.width)*strip.height;offset+=st7796::transfer_pixels){
   const std::size_t count=std::min(st7796::transfer_pixels,std::size_t(strip.width)*strip.height-offset);
   if(!set_address_window(uint16_t(strip.x+offset),uint16_t(strip.y),uint16_t(count),1))return;
   write_pixels(render_strip_.data()+offset,count);service();}
 }
}

void St7796DisplayBackend::present(const Scene&scene,const AssetPool&assets,bool full,const Rect*dirty,std::size_t count){
 if(!initialized_)return;
 if(!render_enabled_){++performance_.skipped_renders;return;}
 const uint64_t started=time_us_64();++performance_.renders;
 if(full||!dirty||count==0){++performance_.full_redraws;++performance_.regions;render_region(scene,assets,{0,0,int16_t(st7796::logical_width),int16_t(st7796::logical_height)});}
 else{++performance_.partial_redraws;performance_.regions+=count;for(std::size_t i=0;i<count;i++)render_region(scene,assets,dirty[i]);}
 const uint64_t elapsed=time_us_64()-started;performance_.total_render_us+=elapsed;
 performance_.max_render_us=std::max(performance_.max_render_us,elapsed);
}

void St7796DisplayBackend::hardware_reset(){gpio_put(hardware::tft_reset,1);sleep_ms(10);gpio_put(hardware::tft_reset,0);sleep_ms(20);gpio_put(hardware::tft_reset,1);sleep_ms(120);}
void St7796DisplayBackend::begin_tft_transfer(){gpio_put(hardware::touch_cs,1);gpio_put(hardware::tft_cs,0);if(!st7796::chip_selects_exclusive(gpio_get(hardware::tft_cs),gpio_get(hardware::touch_cs)))++performance_.chip_select_violations;}
void St7796DisplayBackend::command(uint8_t value){gpio_put(hardware::tft_dc,0);begin_tft_transfer();spi_write_blocking(spi1,&value,1);gpio_put(hardware::tft_cs,1);}
void St7796DisplayBackend::command(uint8_t value,const uint8_t*values,std::size_t length){command(value);data(values,length);}
void St7796DisplayBackend::data(const uint8_t*values,std::size_t length){if(!length)return;gpio_put(hardware::tft_dc,1);begin_tft_transfer();const uint64_t started=time_us_64();spi_write_blocking(spi1,values,length);const uint64_t elapsed=time_us_64()-started;performance_.max_spi_block_us=std::max(performance_.max_spi_block_us,elapsed);gpio_put(hardware::tft_cs,1);}
void St7796DisplayBackend::service()const{if(service_callback_!=nullptr)service_callback_();}
}  // namespace streamdeck::display
