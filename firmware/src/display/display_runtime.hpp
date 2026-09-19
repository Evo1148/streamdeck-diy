#pragma once
#include <array>
#include <cstddef>
#include <cstdint>
#include "display/display_assets.hpp"
#include "display/display_backend.hpp"
namespace streamdeck::display {
class DisplayRuntime{
public:explicit DisplayRuntime(IDisplayBackend&backend,uint32_t boot_session=1):backend_(backend),boot_session_(boot_session){active_.clear();staging_.clear();}
 Error handle(const uint8_t*payload,std::size_t length,uint8_t*out,std::size_t&out_length,uint32_t now_ms);
 void task(uint32_t now_ms);uint32_t active_generation()const{return active_.generation;}const Scene&active_scene()const{return active_;}bool host_online()const{return host_online_;}Error last_error()const{return last_error_;}const AssetPool&assets()const{return assets_;}
 bool hit_test(int16_t x,int16_t y,uint8_t gesture,TouchHit& result)const;
 void set_boot_session(uint32_t value){boot_session_=value?value:1;}
 bool inject_touch(uint16_t region,uint8_t gesture,int16_t x,int16_t y,uint32_t timestamp,uint32_t&event_id,uint8_t*out,std::size_t&length);
private:Error dispatch(const Envelope&e,const uint8_t*b,std::size_t n,uint8_t*out,std::size_t&body,uint32_t now);Scene*target(uint32_t generation);void dirty(const Rect&r);void present(bool full);void response(const Envelope&e,Error error,uint8_t*out,std::size_t&length,std::size_t body);
 IDisplayBackend&backend_;Scene active_{},staging_{};AssetPool assets_{};std::array<Rect,32>dirty_{},pending_dirty_{};std::size_t dirty_count_{},pending_dirty_count_{};uint32_t boot_session_{},last_heartbeat_{};uint64_t epoch_{};int16_t timezone_minutes_{};bool use_24h_{true},staging_active_{},update_active_{},host_online_{},render_pending_{},pending_full_render_{};Error last_error_{Error::None};uint32_t next_event_id_{1};
};
}
