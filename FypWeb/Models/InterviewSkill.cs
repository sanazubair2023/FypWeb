namespace Fyp_Backend.Models;

/// <summary>
/// A snapshot of one worker-offered skill selected by the client for an interview.
/// The selected rows remain attached to the interview as it moves into hiring.
/// </summary>
public sealed class InterviewSkill
{
    public int InterviewId { get; set; }
    public int SkillsId { get; set; }
}
