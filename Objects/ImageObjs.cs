using System.Collections.Generic;
namespace NetworkMonitor.Service.Services.OpenAI;

public class ImageRequest
{
    public string model { get; set; }
    public string prompt { get; set; }
    public int n { get; set; } = 1;
    public string size { get; set; } = "1024x1024";
    public string quality { get; set; } = "standard";
}

public class ImageResponse
{
    public List<ImageData> data { get; set; } = new List<ImageData>();
}

public class ImageData
{
    public string revised_prompt { get; set; } = "";
    public string url { get; set; } = "";
    public string b64_json { get; set; } = "";
}

public class HuggingFaceImageData
{
    public string image_url { get; set; } = "";
    public int image_url_ttl { get; set; }
    public string image_type { get; set; } = "";
}


public class HuggingFaceImageResponse
{
    public List<HuggingFaceImageData> images { get; set; } = new ();

}


public class HuggingFaceImageRequest
{
    public int steps { get; set; } = 20;
    public string response_image_type { get; set; } = "png";
    //public string model_name { get; set; } = "";
    public uint seed { get; set; } =1234567890;
    public string prompt { get; set; } = "";
    public int image_num { get; set; } = 1;
    public int width { get; set; } = 1024;
    public int height { get; set; } = 1024;

}
