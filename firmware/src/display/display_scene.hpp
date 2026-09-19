#pragma once
#include <array>
#include <cstddef>
#include <cstdint>
#include "display/display_link_protocol.hpp"

namespace streamdeck::display {
inline constexpr std::size_t MAX_NODES=128, MAX_TOUCH_REGIONS=32, MAX_ANIMATIONS=32;
inline constexpr std::size_t MAX_KEYFRAMES=8, MAX_STRING_BYTES=4096;
enum class Mode:uint8_t{Offline=0,Dashboard=1,Scene=2};
enum class NodeKind:uint8_t{Group=0,Rectangle=1,Circle=2,Text=3,Icon=4,Image=5,Progress=6,Line=7};
enum class AnimationProperty:uint8_t{X=1,Y=2,Opacity=3,ScaleX=4,ScaleY=5,Rotation=6};
enum class Easing:uint8_t{Linear=0,EaseIn=1,EaseOut=2,EaseInOut=3,Step=4};
enum class TouchMode:uint8_t{Capture=1,PassThrough=2,Ignore=3};
enum class TouchActionKind:uint8_t{None=0,LocalControl=1,HostInteraction=2};
enum TouchGestureMask:uint8_t{TapGesture=1,LongPressGesture=2,SwipeLeftGesture=4,SwipeRightGesture=8};
struct Rect{int16_t x{},y{},width{},height{};};
struct Node{uint16_t id{},parent{};NodeKind kind{};uint8_t flags{};int16_t z{};Rect bounds{};uint16_t fill{},stroke{};uint8_t stroke_width{},corner_radius{},opacity{255},font_id{},font_size{},alignment{};uint16_t icon_id{},asset_id{},progress{},progress_start{},progress_target{};uint32_t progress_started_ms{};int16_t rotation{},scale_x{1000},scale_y{1000};uint16_t text_offset{},text_length{};bool progress_transition{},used{};};
struct TouchRegion{uint16_t id{};Rect bounds{};int16_t priority{};uint8_t gestures{},action{},flags{};uint32_t parameter{};bool used{};};
struct TouchHit{uint16_t region_id{};uint8_t action{};uint32_t parameter{};TouchMode mode{TouchMode::Ignore};};
struct Keyframe{uint16_t time_ms{};int32_t value{};Easing easing{};bool used{};};
struct Animation{uint16_t id{},target{};AnimationProperty property{};uint8_t flags{};uint16_t duration_ms{},delay_ms{},repeat{};std::array<Keyframe,MAX_KEYFRAMES> keyframes{};uint32_t start_ms{};int32_t base_value{};uint8_t keyframe_count{};bool used{},playing{};};

class Scene {
public:
 void clear(uint32_t generation=0,Mode mode=Mode::Dashboard);
 Error define_node(const Node& node); Error patch_node(uint16_t id,const uint8_t* tlv,std::size_t length,uint32_t now_ms=0);
 Error delete_node(uint16_t id); Error set_string(uint16_t id,uint16_t offset,uint16_t total,const uint8_t* data,std::size_t length);
 Error define_region(const TouchRegion& region); Error delete_region(uint16_t id);
 Error define_animation(const Animation& animation); Error define_keyframe(uint16_t id,const Keyframe& keyframe);
 Error play_animation(uint16_t id,bool play,uint32_t now_ms=0); Error delete_animation(uint16_t id);
 void tick_animations(uint32_t now_ms,Rect* dirty,std::size_t& count,std::size_t capacity);
 void tick_progress(uint32_t now_ms,Rect* dirty,std::size_t& count,std::size_t capacity);
 Node* find_node(uint16_t id); const Node* find_node(uint16_t id) const;
 bool text(const Node& node,const uint8_t*& data,std::size_t& length)const;Rect absolute_bounds(const Node& node)const;
 bool hit_test(int16_t x,int16_t y,uint8_t gesture,TouchHit& result)const;
 std::size_t node_count()const;std::size_t region_count()const;std::size_t animation_count()const;
 uint32_t crc32()const; uint32_t generation{};Mode mode{Mode::Offline};
 const std::array<Node,MAX_NODES>& nodes()const{return nodes_;}
 const std::array<TouchRegion,MAX_TOUCH_REGIONS>& regions()const{return regions_;}
private:
 bool valid_bounds(const Rect& r)const; bool parent_valid(uint16_t id,uint16_t parent)const;void compact_strings(uint16_t excluded);
 std::array<Node,MAX_NODES> nodes_{};std::array<TouchRegion,MAX_TOUCH_REGIONS> regions_{};
 std::array<Animation,MAX_ANIMATIONS> animations_{};std::array<uint8_t,MAX_STRING_BYTES> strings_{};uint16_t string_used_{};
};
}
