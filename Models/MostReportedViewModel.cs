using System.Text.Json.Serialization;

namespace appdevPUPSCOUT.Models;

public class RoomReportCount
{
    public int RoomId { get; set; }
    public string RoomName { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class EquipmentReportCount
{
    public int EquipmentId { get; set; }
    public string EquipmentName { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class MostReportedViewModel
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }

    public List<RoomReportCount> RoomCounts { get; set; } = new();
    public List<EquipmentReportCount> EquipmentCounts { get; set; } = new();

    public List<RoomReportCount> TopRooms { get; set; } = new();
    public List<EquipmentReportCount> TopEquipment { get; set; } = new();
    public List<RoomReportCount> Rooms { get; set; } = new();
    public List<EquipmentReportCount> Equipment { get; set; } = new();

    public string RoomCountsJson { get; set; } = "[]";
    public string EquipmentCountsJson { get; set; } = "[]";
    public string TopRoomsJson { get; set; } = "[]";
    public string TopEquipmentJson { get; set; } = "[]";
    public string RoomsJson { get; set; } = "[]";
    public string EquipmentJson { get; set; } = "[]";
}

public class TopReportedViewModel : MostReportedViewModel { }
public class ReportsViewModel : MostReportedViewModel { }
public class TopRoomsEquipmentViewModel : MostReportedViewModel { }
