namespace WhiteBoard.Contracts.Models;

[Flags]
public enum SessionCapability
{
    None          = 0,
    Save          = 1 << 0,
    Collaboration = 1 << 1,
    OCR           = 1 << 2,
    PredictiveInk = 1 << 3,
    Export        = 1 << 4,
    MultiPage     = 1 << 5,
}

public enum SessionRole
{
    Solo        = 0,
    Presenter   = 1,
    Participant = 2,
}
