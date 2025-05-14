using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace NetworkMonitor.Objects;

public class UserAuthInfo {

[Key]
public int Id{get;set;}
    public string? UserID{get;set;}
    public string FusionAppID{get;set;}="";
    public DateTime DateUpdated{get;set;}
    public string ResfreshToken {get;set;}="";
    public string? ClientAppName{get;set;}
    public bool IsAuthenticated {get;set;}
    public bool IsSha3Hash {get;set;}
    
}