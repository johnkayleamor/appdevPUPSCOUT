namespace appdevPUPSCOUT.Models;

public class Building
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<Floor> Floors { get; set; } = new();
}

public class Floor
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int BuildingId { get; set; }
    public Building? Building { get; set; }
    public List<Room> Rooms { get; set; } = new();
}

public class Room
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int FloorId { get; set; }
    public Floor? Floor { get; set; }
    public List<Equipment> Equipment { get; set; } = new();
}

public class Equipment
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int RoomId { get; set; }
    public Room? Room { get; set; }
}