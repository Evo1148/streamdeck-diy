#include "display/display_runtime.hpp"
#include <cstring>
namespace streamdeck::display {
namespace {constexpr std::size_t NODE_BODY=34,REGION_BODY=20;}
void DisplayRuntime::response(const Envelope&e,Error error,uint8_t*out,std::size_t&length,std::size_t body){Envelope r{DISPLAY_LINK_MAJOR,DISPLAY_LINK_MINOR,e.opcode,error==Error::None?uint8_t(0):uint8_t(ResponseError),e.generation};encode_envelope(out,r);if(error!=Error::None){out[DISPLAY_ENVELOPE_SIZE]=uint8_t(error);body=1;}length=DISPLAY_ENVELOPE_SIZE+body;last_error_=error;}
Error DisplayRuntime::handle(const uint8_t*p,std::size_t n,uint8_t*out,std::size_t&out_len,uint32_t now){out_len=0;Envelope e{};if(!decode_envelope(p,n,e)){return Error::InvalidState;}std::memset(out,0,DISPLAY_ENVELOPE_SIZE+DISPLAY_BODY_MAX);if(e.major!=DISPLAY_LINK_MAJOR){response(e,Error::UnsupportedVersion,out,out_len,0);return Error::UnsupportedVersion;}std::size_t body=0;Error error=dispatch(e,p+DISPLAY_ENVELOPE_SIZE,n-DISPLAY_ENVELOPE_SIZE,out+DISPLAY_ENVELOPE_SIZE,body,now);response(e,error,out,out_len,body);return error;}
Scene* DisplayRuntime::target(uint32_t g){if(staging_active_&&staging_.generation==g)return &staging_;if(active_.generation==g)return &active_;return nullptr;}
void DisplayRuntime::dirty(const Rect&r){if(dirty_count_<dirty_.size())dirty_[dirty_count_++]=r;}
void DisplayRuntime::present(bool full){
 if(full){render_pending_=true;pending_full_render_=true;pending_dirty_count_=0;dirty_count_=0;return;}
 if(dirty_count_==0)return;
 render_pending_=true;
 if(!pending_full_render_){
  for(std::size_t i=0;i<dirty_count_;++i){
   if(pending_dirty_count_==pending_dirty_.size()){pending_full_render_=true;pending_dirty_count_=0;break;}
   pending_dirty_[pending_dirty_count_++]=dirty_[i];
  }
 }
 dirty_count_=0;
}
Error DisplayRuntime::dispatch(const Envelope&e,const uint8_t*b,std::size_t n,uint8_t*out,std::size_t&body,uint32_t now){
 switch(static_cast<Opcode>(e.opcode)){
 case Opcode::GetInfo: {
  if(n!=0)return Error::InvalidState;
  write_u16(out,480);write_u16(out+2,320);out[4]=1;out[5]=backend_.physical()?1:0;out[6]=backend_.physical()&&backend_.state()?3:0;out[7]=0;write_u32(out+8,0xFF);write_u32(out+12,0x3F);write_u16(out+16,1);write_u16(out+18,1);write_u16(out+20,MAX_NODES);write_u16(out+22,MAX_TOUCH_REGIONS);write_u16(out+24,MAX_ANIMATIONS);out[26]=MAX_KEYFRAMES;out[27]=0;write_u16(out+28,MAX_STRING_BYTES);write_u32(out+30,ASSET_POOL_BYTES);write_u32(out+34,MAX_SINGLE_ASSET_BYTES);write_u32(out+38,boot_session_);body=42;return Error::None;
 }
 case Opcode::GetStatus: {
  if(n!=0)return Error::InvalidState;
  write_u32(out,active_.generation);write_u32(out+4,staging_active_?staging_.generation:0);out[8]=uint8_t(active_.mode);out[9]=host_online_;write_u16(out+10,uint16_t(active_.node_count()));write_u16(out+12,uint16_t(active_.region_count()));write_u16(out+14,uint16_t(active_.animation_count()));write_u32(out+16,uint32_t(assets_.bytes_used()));out[20]=assets_.active_transfers();out[21]=uint8_t(last_error_);out[22]=backend_.state();out[23]=0;write_u32(out+24,active_.crc32());body=28;return Error::None;
 }
 case Opcode::BeginSync:if(n!=1||e.generation==0)return Error::InvalidGeneration;if(staging_active_)return Error::Busy;if(b[0]>uint8_t(Mode::Scene))return Error::InvalidState;staging_.clear(e.generation,static_cast<Mode>(b[0]));staging_active_=true;return Error::None;
 case Opcode::CommitSync:if(n!=0||!staging_active_||e.generation!=staging_.generation)return Error::InvalidGeneration;active_=staging_;staging_active_=false;update_active_=false;present(true);return Error::None;
 case Opcode::CancelSync:if(n!=0||!staging_active_||e.generation!=staging_.generation)return Error::InvalidGeneration;staging_.clear();staging_active_=false;return Error::None;
 case Opcode::BeginUpdate:if(n!=0||staging_active_||update_active_||e.generation!=active_.generation)return Error::InvalidGeneration;update_active_=true;dirty_count_=0;return Error::None;
 case Opcode::CommitUpdate:if(n!=0||!update_active_||e.generation!=active_.generation)return Error::InvalidState;update_active_=false;present(false);return Error::None;
 case Opcode::DefineNode:{if(n!=NODE_BODY||!staging_active_)return Error::InvalidState;Scene*s=target(e.generation);if(!s)return Error::InvalidGeneration;Node v{};v.id=read_u16(b);v.parent=read_u16(b+2);v.kind=static_cast<NodeKind>(b[4]);v.flags=b[5];v.z=read_i16(b+6);v.bounds={read_i16(b+8),read_i16(b+10),read_i16(b+12),read_i16(b+14)};v.fill=read_u16(b+16);v.stroke=read_u16(b+18);v.stroke_width=b[20];v.corner_radius=b[21];v.opacity=b[22];v.font_id=b[23];v.font_size=b[24];v.alignment=b[25];v.icon_id=read_u16(b+26);v.asset_id=read_u16(b+28);v.progress=read_u16(b+30);v.progress_start=v.progress;v.progress_target=v.progress;v.rotation=read_i16(b+32);return s->define_node(v);}
 case Opcode::PatchNode:{if(n<2||(!staging_active_&&!update_active_))return Error::InvalidState;Scene*s=target(e.generation);if(!s)return Error::InvalidGeneration;Node*node=s->find_node(read_u16(b));if(!node)return Error::InvalidNode;Rect old=s->absolute_bounds(*node);Error er=s->patch_node(node->id,b+2,n-2,now);if(er==Error::None&&s==&active_){dirty(old);dirty(s->absolute_bounds(*node));}return er;}
 case Opcode::DeleteNode:{if(n!=2||(!staging_active_&&!update_active_))return Error::InvalidState;Scene*s=target(e.generation);if(!s)return Error::InvalidGeneration;Node*node=s->find_node(read_u16(b));if(!node)return Error::InvalidNode;Rect old=s->absolute_bounds(*node);Error er=s->delete_node(node->id);if(er==Error::None&&s==&active_)dirty(old);return er;}
 case Opcode::SetStringFragment:{if(n<6||(!staging_active_&&!update_active_))return Error::InvalidState;Scene*s=target(e.generation);if(!s)return Error::InvalidGeneration;Error er=s->set_string(read_u16(b),read_u16(b+2),read_u16(b+4),b+6,n-6);if(er==Error::None&&s==&active_){const Node*node=s->find_node(read_u16(b));if(node)dirty(s->absolute_bounds(*node));}return er;}
 case Opcode::DefineTouchRegion:{if(n!=REGION_BODY||!staging_active_)return Error::InvalidState;Scene*s=target(e.generation);if(!s)return Error::InvalidGeneration;TouchRegion r{};r.id=read_u16(b);r.bounds={read_i16(b+2),read_i16(b+4),read_i16(b+6),read_i16(b+8)};r.priority=read_i16(b+10);r.gestures=b[12];r.action=b[13];r.flags=b[14];r.parameter=read_u32(b+16);return s->define_region(r);}
 case Opcode::DeleteTouchRegion:{if(n!=2||(!staging_active_&&!update_active_))return Error::InvalidState;Scene*s=target(e.generation);return s?s->delete_region(read_u16(b)):Error::InvalidGeneration;}
 case Opcode::DefineAnimation:{if(n!=12||!staging_active_)return Error::InvalidState;Scene*s=target(e.generation);if(!s)return Error::InvalidGeneration;Animation a{};a.id=read_u16(b);a.target=read_u16(b+2);a.property=static_cast<AnimationProperty>(b[4]);a.flags=b[5];a.duration_ms=read_u16(b+6);a.delay_ms=read_u16(b+8);a.repeat=read_u16(b+10);return s->define_animation(a);}
 case Opcode::DefineKeyframe:{if(n!=9||!staging_active_)return Error::InvalidState;Scene*s=target(e.generation);if(!s)return Error::InvalidGeneration;Keyframe k{};k.time_ms=read_u16(b+2);k.value=static_cast<int32_t>(read_u32(b+4));k.easing=static_cast<Easing>(b[8]);return s->define_keyframe(read_u16(b),k);}
 case Opcode::PlayAnimation:case Opcode::StopAnimation:{if(n!=2)return Error::InvalidAnimation;Scene*s=target(e.generation);return s?s->play_animation(read_u16(b),static_cast<Opcode>(e.opcode)==Opcode::PlayAnimation,now):Error::InvalidGeneration;}
 case Opcode::DeleteAnimation:{if(n!=2)return Error::InvalidAnimation;Scene*s=target(e.generation);return s?s->delete_animation(read_u16(b)):Error::InvalidGeneration;}
 case Opcode::AssetBegin:if(n!=19)return Error::InvalidState;return assets_.begin(read_u16(b),read_u16(b+2),static_cast<AssetFormat>(b[4]),read_u16(b+5),read_u16(b+7),read_u32(b+9),read_u32(b+13));
 case Opcode::AssetChunk:if(n<6)return Error::InvalidState;return assets_.chunk(read_u16(b),read_u32(b+2),b+6,n-6);
 case Opcode::AssetCommit:{if(n!=2)return Error::InvalidState;const Error result=assets_.commit(read_u16(b));if(result==Error::None){const uint16_t id=assets_.last_committed_id();for(const auto&node:active_.nodes())if(node.used&&node.kind==NodeKind::Image&&node.asset_id==id)dirty(active_.absolute_bounds(node));if(dirty_count_)present(false);}return result;}
 case Opcode::AssetRelease:if(n!=2)return Error::InvalidState;return assets_.release(read_u16(b));
 case Opcode::ClockSync:if(n!=11)return Error::InvalidState;epoch_=uint64_t(read_u32(b))|(uint64_t(read_u32(b+4))<<32);timezone_minutes_=read_i16(b+8);use_24h_=b[10]!=0;return Error::None;
 case Opcode::Heartbeat:if(n!=0)return Error::InvalidState;last_heartbeat_=now;host_online_=true;return Error::None;
 default:return Error::UnsupportedOpcode;
 }}
void DisplayRuntime::task(uint32_t now){
 if(host_online_&&uint32_t(now-last_heartbeat_)>5000)host_online_=false;
 std::size_t count=0;active_.tick_animations(now,dirty_.data(),count,dirty_.size());active_.tick_progress(now,dirty_.data(),count,dirty_.size());if(count){dirty_count_=count;present(false);}
 if(!render_pending_)return;
 const bool full=pending_full_render_;
 const std::size_t regions=pending_dirty_count_;
 const std::array<Rect,32> requested=pending_dirty_;
 render_pending_=false;pending_full_render_=false;pending_dirty_count_=0;
 backend_.present(active_,assets_,full,requested.data(),regions);
}
bool DisplayRuntime::inject_touch(uint16_t region,uint8_t gesture,int16_t x,int16_t y,uint32_t timestamp,uint32_t&event_id,uint8_t*out,std::size_t&length){if(active_.generation==0)return false;event_id=next_event_id_++;Envelope e{DISPLAY_LINK_MAJOR,DISPLAY_LINK_MINOR,uint8_t(Opcode::TouchEvent),0,active_.generation};encode_envelope(out,e);write_u32(out+8,event_id);write_u16(out+12,region);out[14]=gesture;out[15]=0;write_i16(out+16,x);write_i16(out+18,y);write_u32(out+20,timestamp);length=24;return true;}
bool DisplayRuntime::hit_test(int16_t x,int16_t y,uint8_t gesture,TouchHit&result)const{return active_.generation!=0&&active_.hit_test(x,y,gesture,result);}
}
