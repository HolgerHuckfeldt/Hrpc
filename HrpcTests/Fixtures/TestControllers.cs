using HrpcCommon.Attribute;
using HrpcCommon.Models;
using HrpcCore.Attributes;

namespace HrpcTests.Fixtures;

[HrpcController("TestInvoker")]
public class TestInvokerController
{
    [HrpcMethod("Echo")]
    public string Echo(string message) => message;

    [HrpcMethod("Add")]
    public int Add(int a, int b) => a + b;

    [HrpcMethod("EchoAsync")]
    public async Task<string> EchoAsync(string message)
    {
        await Task.Delay(10);
        return message;
    }

    [HrpcMethod("AddAsync")]
    public async Task<int> AddAsync(int a, int b)
    {
        await Task.Delay(10);
        return a + b;
    }

    [HrpcMethod("VoidMethod")]
    public void VoidMethod(string data) { }

    [HrpcMethod("WithCancellation")]
    public async Task<string> WithCancellation(string input, CancellationToken ct)
    {
        await Task.Delay(10, ct);
        return input;
    }

    [HrpcMethod("ComplexReturn")]
    public TestDto ComplexReturn(int id) => new() { Id = id, Name = "Test" };

    [HrpcMethod("NoParams")]
    public string NoParams() => "Hello World";

    [HrpcMethod("Secured")]
    [HrpcAuthorise]
    public string Secured(string data) => data;

    [HrpcMethod("SecuredWithRole")]
    [HrpcAuthorise(Role = "Admin")]
    public string SecuredWithRole(string data) => data;

    [HrpcMethod("StreamNumbers")]
    public async IAsyncEnumerable<int> StreamNumbers(int count, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        for (int i = 0; i < count; i++)
        {
            await Task.Delay(1, ct);
            yield return i;
        }
    }

    [HrpcMethod("StreamNumbersTask")]
    public async Task<IAsyncEnumerable<int>> StreamNumbersTask(int count, CancellationToken ct = default)
        => StreamNumbers(count, ct);

    [HrpcMethod("UploadBlob")]
    public string UploadBlob(byte[] data, string filename)
        => $"Received {data.Length} bytes for {filename}";

    [HrpcMethod("DownloadBlob")]
    public byte[] DownloadBlob(string name)
        => System.Text.Encoding.UTF8.GetBytes($"Blob content for {name}");

    [HrpcMethod("UploadAndProcess")]
    public async Task<int> UploadAndProcess(byte[] data, CancellationToken ct)
    {
        await Task.Delay(1, ct);
        return data.Length;
    }

    [HrpcMethod("DownloadStream")]
    public System.IO.Stream DownloadStream(string name)
    {
        var content = System.Text.Encoding.UTF8.GetBytes($"Streamed content for {name}");
        return new System.IO.MemoryStream(content);
    }
}

[HrpcDataContract]
public class TestDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}