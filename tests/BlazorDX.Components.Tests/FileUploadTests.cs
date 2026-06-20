using BlazorDX.Components;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Xunit;

namespace BlazorDX.Components.Tests;

/// <summary>DxFileUpload: selecting files lists them; removing drops one.</summary>
public sealed class FileUploadTests : TestContext
{
    [Fact]
    public void Selecting_files_lists_them_and_raises_the_callback()
    {
        IReadOnlyList<IBrowserFile>? captured = null;
        IRenderedComponent<DxFileUpload> upload = RenderComponent<DxFileUpload>(p => p
            .Add(u => u.OnFilesSelected, EventCallback.Factory.Create<IReadOnlyList<IBrowserFile>>(this, f => captured = f)));

        upload.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromText("hello", "notes.txt"),
            InputFileContent.CreateFromText("a,b,c", "data.csv"));

        Assert.Equal(2, upload.FindAll(".dx-upload-item").Count);
        Assert.Contains("notes.txt", upload.Markup);
        Assert.Contains("data.csv", upload.Markup);
        Assert.Equal(2, captured?.Count);
    }

    [Fact]
    public void Removing_a_file_drops_it_from_the_list()
    {
        IRenderedComponent<DxFileUpload> upload = RenderComponent<DxFileUpload>();

        upload.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromText("a", "a.txt"),
            InputFileContent.CreateFromText("b", "b.txt"));
        Assert.Equal(2, upload.FindAll(".dx-upload-item").Count);

        upload.FindAll(".dx-upload-remove")[0].Click();
        Assert.Single(upload.FindAll(".dx-upload-item"));
    }

    [Fact]
    public void Renders_a_keyboard_focusable_native_file_input()
    {
        IRenderedComponent<DxFileUpload> upload = RenderComponent<DxFileUpload>(p => p
            .Add(u => u.Accept, ".pdf,image/*"));

        var input = upload.Find("input[type=file]");
        Assert.Equal(".pdf,image/*", input.GetAttribute("accept"));
        Assert.True(input.HasAttribute("multiple"));
    }
}
