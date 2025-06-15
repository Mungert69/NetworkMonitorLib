// A class called KemExtension containing fields. GroupHexID, GroupID, KeyShareLength and Data
namespace NetworkMonitor.Objects{
    public class KemExtension
{
    public KemExtension(){}
    public string GroupHexStringID { get; set; }= "0x0000";
    public int GroupID { get; set; }
    public int KeyShareLength { get; set; }
    public byte[] Data { get; set; }= new byte[0];
    public bool IsQuantumSafe { get; set; }= false;
    public bool LongServerHello { get; set; }= false;
}
}
