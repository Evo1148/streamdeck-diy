#pragma once
#include <array>
#include <cstddef>
#include "display/display_assets.hpp"
#include "display/display_scene.hpp"
namespace streamdeck::display {
class IDisplayBackend { public: virtual ~IDisplayBackend()=default; virtual void present(const Scene&,const AssetPool&,bool full,const Rect*,std::size_t)=0; virtual uint8_t state()const=0; virtual bool physical()const=0; };
class NullDisplayBackend final:public IDisplayBackend{
public:void present(const Scene&scene,const AssetPool&assets,bool full,const Rect*dirty,std::size_t count)override;uint8_t state()const override{return 0;}bool physical()const override{return false;}std::size_t draw_count()const{return draw_count_;}std::size_t last_node_count()const{return last_node_count_;}std::size_t dirty_count()const{return dirty_count_;}bool last_full()const{return last_full_;}
private:std::size_t draw_count_{},last_node_count_{},dirty_count_{};bool last_full_{};
};
}
