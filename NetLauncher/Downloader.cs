using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace NetLauncher
{
    public class Downloader
    {
        private static readonly HttpClient _http = new HttpClient();

        // Descarga un archivo y verifica su SHA1
        // Retorna true si todo salió bien
        public async Task<bool> DownloadFileAsync(string url, string destPath, string expectedSha1 = null)
        {
            // Crear carpetas si no existen
            Directory.CreateDirectory(Path.GetDirectoryName(destPath));

            // Si el archivo ya existe y el SHA1 es correcto, no lo volvemos a descargar
            if (File.Exists(destPath) && expectedSha1 != null)
            {
                if (VerifySha1(destPath, expectedSha1))
                    return true; // Ya está descargado y es válido
            }

            byte[] data = await _http.GetByteArrayAsync(url);

            // Verificar SHA1 antes de guardar
            if (expectedSha1 != null)
            {
                string actualSha1 = ComputeSha1(data);
                if (!actualSha1.Equals(expectedSha1, StringComparison.OrdinalIgnoreCase))
                    throw new Exception($"SHA1 inválido para {Path.GetFileName(destPath)}\nEsperado: {expectedSha1}\nObtenido: {actualSha1}");
            }

            File.WriteAllBytes(destPath, data);
            return true;
        }

        private string ComputeSha1(byte[] data)
        {
            using (var sha1 = SHA1.Create())
            {
                byte[] hash = sha1.ComputeHash(data);
                return BitConverter.ToString(hash).Replace("-", "").ToLower();
            }
        }

        private bool VerifySha1(string filePath, string expectedSha1)
        {
            using (var sha1 = SHA1.Create())
            using (var stream = File.OpenRead(filePath))
            {
                byte[] hash = sha1.ComputeHash(stream);
                string actual = BitConverter.ToString(hash).Replace("-", "").ToLower();
                return actual.Equals(expectedSha1, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}