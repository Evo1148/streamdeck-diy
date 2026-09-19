#include "display/display_backend.hpp"
namespace streamdeck::display {void NullDisplayBackend::present(const Scene&s,const AssetPool&,bool full,const Rect*,std::size_t count){++draw_count_;last_node_count_=s.node_count();dirty_count_=count;last_full_=full;}}
