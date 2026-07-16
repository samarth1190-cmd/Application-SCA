using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System;
using System.Text;
using System.Collections.Generic;
using System.Text.Json;

namespace Aplicacion_SCA.Services;

public class SharePointService
{
    private const string ClientId = "9a07fccf-3bfa-4347-bca3-9fd5ed0dfe09";    // <-- Reemplaza con tu Client ID nuevo
    private const string TenantId = "d852d5cd-724c-4128-8812-ffa5db3f8507";    // <-- Reemplaza con tu Tenant ID nuevo
    private const string ClientSecret = "4rb8Q~x1LDeW6r~PLOpll5VW3kWZZ5CG7tmhFaZa";   // <-- Reemplaza con tu Client Secret nuevo 
    private const string SiteId = "shiftup.sharepoint.com,0dee19d4-4ed1-4906-a254-42307b804d54,1c3588cf-a23a-42e6-885f-057fa2fcc8ef";

    public async Task<string> ConseguirTokenSilenciosoAsync()
    {
        string urlAzure = $"https://login.microsoftonline.com/{TenantId}/oauth2/v2.0/token";
        using var client = new HttpClient();
        var cuerpoPeticion = new Dictionary<string, string>
        {
            { "client_id", ClientId },
            { "client_secret", ClientSecret },
            { "scope", "https://graph.microsoft.com/.default" },
            { "grant_type", "client_credentials" }
        };

        var content = new FormUrlEncodedContent(cuerpoPeticion);
        try
        {
            var response = await client.PostAsync(urlAzure, content);
            string jsonRespuesta = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(jsonRespuesta);
                return doc.RootElement.GetProperty("access_token").GetString();
            }
            else
            {
                throw new Exception($"Azure denegó el acceso: {jsonRespuesta}");
            }
        }
        catch (Exception ex)
        {
            throw new Exception("Error al conseguir el token por HTTP: " + ex.Message);
        }
    }

    public async Task<byte[]> DescargarExcelAsync(string nombreArchivo)
    {
        string token = await ConseguirTokenSilenciosoAsync();
        string urlCompleta = $"https://graph.microsoft.com/v1.0/sites/{SiteId}/drive/root:/{nombreArchivo}:/content";

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.GetAsync(urlCompleta);

        if (response.IsSuccessStatusCode) return await response.Content.ReadAsByteArrayAsync();
        else throw new Exception($"Error al descargar '{nombreArchivo}': {await response.Content.ReadAsStringAsync()}");
    }

    public async Task<byte[]> DescargarExcelConTokenAsync(string nombreArchivo, string token)
    {
        string urlCompleta = $"https://graph.microsoft.com/v1.0/sites/{SiteId}/drive/root:/{nombreArchivo}:/content";
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync(urlCompleta);
        if (response.IsSuccessStatusCode) return await response.Content.ReadAsByteArrayAsync();
        else throw new Exception($"Error al descargar '{nombreArchivo}': {await response.Content.ReadAsStringAsync()}");
    }

    public async Task<bool> InsertarEnListaAsync(string listId, string datosJson)
    {
        string token = await ConseguirTokenSilenciosoAsync();

        string urlCompleta = $"https://graph.microsoft.com/v1.0/sites/{SiteId}/lists/{listId}/items";

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var content = new StringContent(datosJson, Encoding.UTF8, "application/json");

        var response = await client.PostAsync(urlCompleta, content);

        if (response.IsSuccessStatusCode)
        {
            return true;
        }
        else
        {
            string error = await response.Content.ReadAsStringAsync();
            throw new Exception($"Error al insertar en lista ({response.StatusCode}): {error}");
        }
    }

    public async Task<string> SubirArchivoADocumentosAsync(string rutaCarpetaSharePoint, string nombreArchivo, byte[] archivoBytes)
    {
        string token = await ConseguirTokenSilenciosoAsync();
        string rutaCompleta = $"{rutaCarpetaSharePoint.TrimEnd('/')}/{nombreArchivo}";
        string urlCompleta = $"https://graph.microsoft.com/v1.0/sites/{SiteId}/drive/root:/{rutaCompleta}:/content";

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var content = new ByteArrayContent(archivoBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");

        var response = await client.PutAsync(urlCompleta, content);

        if (response.IsSuccessStatusCode)
        {
            string json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("webUrl").GetString();
        }
        else
        {
            throw new Exception($"Error al subir imagen '{nombreArchivo}': {await response.Content.ReadAsStringAsync()}");
        }
    }

    public async Task<List<string>> ObtenerPdfsEnCarpetaAsync(string nombreCarpeta)
    {
        string token = await ConseguirTokenSilenciosoAsync();
        string urlCompleta = $"https://graph.microsoft.com/v1.0/sites/{SiteId}/drive/root:/{nombreCarpeta}:/children?$select=name";
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.GetAsync(urlCompleta);
        var listaArchivos = new List<string>();

        if (response.IsSuccessStatusCode)
        {
            string json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("value", out var valueElement))
            {
                foreach (var item in valueElement.EnumerateArray())
                {
                    if (item.TryGetProperty("name", out var nameElement))
                    {
                        string name = nameElement.GetString();
                        if (!string.IsNullOrEmpty(name) && name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) listaArchivos.Add(name);
                    }
                }
            }
        }
        return listaArchivos;
    }

    public async Task<List<string>> ObtenerExcelsEnCarpetaAsync(string nombreCarpeta)
    {
        string token = await ConseguirTokenSilenciosoAsync();
        string urlCompleta = $"https://graph.microsoft.com/v1.0/sites/{SiteId}/drive/root:/{nombreCarpeta}:/children?$select=name";
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.GetAsync(urlCompleta);
        var listaArchivos = new List<string>();

        if (response.IsSuccessStatusCode)
        {
            string json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("value", out var valueElement))
            {
                foreach (var item in valueElement.EnumerateArray())
                {
                    if (item.TryGetProperty("name", out var nameElement))
                    {
                        string name = nameElement.GetString();
                        if (!string.IsNullOrEmpty(name) && name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) listaArchivos.Add(name);
                    }
                }
            }
        }
        return listaArchivos;
    }
}