using System.ComponentModel.DataAnnotations;

namespace appdevPUPSCOUT.Models;

public class TicketCreateViewModel
{
    [Required]
    public TicketCategory Category { get; set; }

    [Required]
    [Range(1, int.MaxValue, ErrorMessage = "Select a building.")]
    public int BuildingId { get; set; }

    [Required]
    [Range(1, int.MaxValue, ErrorMessage = "Select a floor.")]
    public int FloorId { get; set; }

    [Required]
    [Range(1, int.MaxValue, ErrorMessage = "Select a room.")]
    public int RoomId { get; set; }

    [Required]
    [Range(1, int.MaxValue, ErrorMessage = "Select equipment.")]
    public int EquipmentId { get; set; }

    [Required]
    [StringLength(2000, MinimumLength = 10)]
    public string Description { get; set; } = string.Empty;

    public IFormFile? Image { get; set; }
}
