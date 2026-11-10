namespace appdevPUPSCOUT.Models;

public enum TicketCategory { Hardware, Facility }

public enum TicketStatus
{
    Pending, Assigned, InProgress, PendingVerification, Closed, InvalidSpam
}