namespace RedControlUtility.Services;

public static class ShaderCacheService
{
    /// <summary>Apaga os caches de shader em disco (AMD DxCache/GLCache e D3DSCache). Retorna a quantidade de arquivos removidos.</summary>
    public static int ClearShaderCaches()
    {
        int deleted = 0;
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string[] dirs =
        {
            Path.Combine(local, "AMD", "DxCache"),
            Path.Combine(local, "AMD", "DxcCache"),
            Path.Combine(local, "AMD", "GLCache"),
            Path.Combine(local, "AMD", "VkCache"),
            Path.Combine(local, "D3DSCache")
        };

        foreach (string dir in dirs)
        {
            if (!Directory.Exists(dir)) continue;
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).ToList();
            }
            catch
            {
                continue;
            }

            foreach (string file in files)
            {
                try
                {
                    File.Delete(file);
                    deleted++;
                }
                catch
                {
                    // arquivo em uso
                }
            }
        }

        return deleted;
    }
}
