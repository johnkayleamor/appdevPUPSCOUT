using appdevPUPSCOUT.Data;
using appdevPUPSCOUT.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace appdevPUPSCOUT.Services;

public class LookupCacheService(ApplicationDbContext dbContext, IMemoryCache cache)
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly IMemoryCache _cache = cache;

    private const string BuildingsKey = "lookup:buildings";
    private const string FloorsKey = "lookup:floors";
    private const string RoomsKey = "lookup:rooms";
    private const string EquipmentKey = "lookup:equipment";

    public List<Building> GetBuildings()
    {
        return _cache.GetOrCreate(BuildingsKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
            return _dbContext.Buildings.OrderBy(b => b.Name).ToList();
        })!;
    }

    public List<Floor> GetFloors()
    {
        return _cache.GetOrCreate(FloorsKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
            return _dbContext.Floors.Include(f => f.Building).OrderBy(f => f.Name).ToList();
        })!;
    }

    public List<Room> GetRooms()
    {
        return _cache.GetOrCreate(RoomsKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
            return _dbContext.Rooms.Include(r => r.Floor).ThenInclude(f => f!.Building).OrderBy(r => r.Name).ToList();
        })!;
    }

    public List<Equipment> GetEquipment()
    {
        return _cache.GetOrCreate(EquipmentKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
            return _dbContext.Equipment.Include(e => e.Room).OrderBy(e => e.Name).ToList();
        })!;
    }

    public void Invalidate()
    {
        _cache.Remove(BuildingsKey);
        _cache.Remove(FloorsKey);
        _cache.Remove(RoomsKey);
        _cache.Remove(EquipmentKey);
    }
}
