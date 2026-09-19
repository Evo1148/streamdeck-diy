#pragma once
#include <array>
#include <cstddef>
#include <cstdint>
#include "display/display_link_protocol.hpp"
namespace streamdeck::display {
inline constexpr std::size_t ASSET_POOL_BYTES=64*1024,MAX_SINGLE_ASSET_BYTES=32*1024,MAX_ASSETS=8;
enum class AssetFormat:uint8_t{Rgb565Raw=1};
struct Asset{uint16_t id{};AssetFormat format{};uint16_t width{},height{};uint32_t offset{},size{},crc{};bool used{};};
class AssetPool{
public:Error begin(uint16_t transfer,uint16_t asset,AssetFormat format,uint16_t width,uint16_t height,uint32_t total,uint32_t crc);Error chunk(uint16_t transfer,uint32_t offset,const uint8_t*data,std::size_t length);Error commit(uint16_t transfer);Error release(uint16_t asset);std::size_t bytes_used()const{return used_;}uint8_t active_transfers()const{return receiving_?1:0;}const Asset* find(uint16_t asset)const;const uint8_t* data(const Asset& asset)const;uint16_t last_committed_id()const{return last_committed_id_;}
private:void compact(uint16_t excluded);uint32_t crc32(const uint8_t*d,std::size_t n)const;std::array<uint8_t,ASSET_POOL_BYTES>pool_{};std::array<uint8_t,MAX_SINGLE_ASSET_BYTES>incoming_{};std::array<Asset,MAX_ASSETS>assets_{};std::size_t used_{};uint16_t transfer_{},asset_{};AssetFormat format_{};uint16_t width_{},height_{};uint32_t expected_{},received_{},crc_{};uint16_t last_committed_id_{};bool receiving_{};
};
}
