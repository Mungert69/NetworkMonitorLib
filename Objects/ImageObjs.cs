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

public class HuggingFaceImageRequest
{
    public string response_image_type { get; set; } = "png";
    public string prompt { get; set; } = "";
    public uint seed { get; set; } = 1234567890;
    public int steps { get; set; } = 4;
    public int width { get; set; } = 512;
    public int height { get; set; } = 512;
    public int image_num { get; set; } = 1;
}



public class HuggingFaceImageResponse
{
    public List<HuggingFaceImageData> images { get; set; } = new();

}




public class HuggingFaceAsyncTaskResponse
{
    public HuggingFaceTask task { get; set; }
    public List<HuggingFaceImageData> images { get; set; }
}

public class HuggingFaceTask
{
    public string task_id { get; set; }
}

public class HuggingFaceAsyncResult
{
    public string status { get; set; }
    public List<HuggingFaceImageData> images { get; set; }
}

public class NovitaImageRequest
{
    public string model_name { get; set; }
    public string prompt { get; set; }
    public string negative_prompt { get; set; }
    public int width { get; set; }
    public int height { get; set; }
    public string sampler_name { get; set; }
    public double guidance_scale { get; set; }
    public int steps { get; set; }
    public int image_num { get; set; }
    public int clip_skip { get; set; }
    public int seed { get; set; }
}

// Root object for Novita API request
public class NovitaImageRequestRoot
{
    public NovitaImageRequest request { get; set; }
}

public class NovitaTaskResponse
{
    public string task_id { get; set; }
}

// Full Novita response structure
public class NovitaFullResponse
{
    public NovitaExtra extra { get; set; }
    public NovitaTask task { get; set; }
    public List<NovitaImage> images { get; set; }
    public List<object> videos { get; set; }
    public List<object> audios { get; set; }
}

public class NovitaExtra
{
    public string seed { get; set; }
    public bool enable_nsfw_detection { get; set; }
    public NovitaDebugInfo debug_info { get; set; }
}

public class NovitaDebugInfo
{
    public string request_info { get; set; }
    public string submit_time_ms { get; set; }
    public string execute_time_ms { get; set; }
    public string complete_time_ms { get; set; }
}

public class NovitaTask
{
    public string task_id { get; set; }
    public string task_type { get; set; }
    public string status { get; set; }
    public string reason { get; set; }
    public int eta { get; set; }
    public int progress_percent { get; set; }
}

public class NovitaImage
{
    public string image_url { get; set; }
    // Add other fields if present
}
