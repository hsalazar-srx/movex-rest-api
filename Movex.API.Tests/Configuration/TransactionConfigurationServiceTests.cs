using Movex.API.Infrastructure.Configuration;

namespace Movex.API.Tests.Configuration;

public class TransactionConfigurationServiceTests : IDisposable
{
    private readonly string _tempDir;

    public TransactionConfigurationServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"movex-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private void WriteConfig(string fileName, string json)
        => File.WriteAllText(Path.Combine(_tempDir, fileName), json);

    private TransactionConfigurationService Build() =>
        new(_tempDir);

    private const string ValidPms100Mi = """
        {
          "program": "PMS100MI",
          "library": "MVXCOBJ",
          "description": "Manufacturing Order MI",
          "transactions": [
            {
              "name": "SelMaterials",
              "description": "List materials",
              "returnsList": true,
              "fields": [
                { "name": "CONO", "position": 1, "length": 3,  "type": "numeric", "required": true  },
                { "name": "FACI", "position": 2, "length": 3,  "type": "text",    "required": true  },
                { "name": "PRNO", "position": 3, "length": 15, "type": "text",    "required": true  },
                { "name": "MFNO", "position": 4, "length": 10, "type": "numeric", "required": true  }
              ],
              "responseFields": [
                { "name": "MSEQ", "position": 1, "length": 4,  "type": "text" },
                { "name": "ITNO", "position": 2, "length": 15, "type": "text" }
              ]
            }
          ]
        }
        """;

    [Fact]
    public void Loads_valid_config_and_returns_program()
    {
        WriteConfig("PMS100MI.json", ValidPms100Mi);
        using var svc = Build();

        var prog = svc.GetProgram("PMS100MI");

        prog.Should().NotBeNull();
        prog!.Program.Should().Be("PMS100MI");
    }

    [Fact]
    public void GetTransaction_returns_correct_transaction()
    {
        WriteConfig("PMS100MI.json", ValidPms100Mi);
        using var svc = Build();

        var tx = svc.GetTransaction("PMS100MI", "SelMaterials");

        tx.Should().NotBeNull();
        tx!.Name.Should().Be("SelMaterials");
        tx.ReturnsList.Should().BeTrue();
    }

    [Fact]
    public void GetTransaction_is_case_insensitive()
    {
        WriteConfig("PMS100MI.json", ValidPms100Mi);
        using var svc = Build();

        var tx = svc.GetTransaction("PMS100MI", "selmaterials");

        tx.Should().NotBeNull();
    }

    [Fact]
    public void GetProgram_returns_null_for_unknown_program()
    {
        using var svc = Build();

        svc.GetProgram("UNKNOWN").Should().BeNull();
    }

    [Fact]
    public void GetTransaction_returns_null_for_unknown_transaction()
    {
        WriteConfig("PMS100MI.json", ValidPms100Mi);
        using var svc = Build();

        svc.GetTransaction("PMS100MI", "NoSuchTx").Should().BeNull();
    }

    [Fact]
    public void GetPrograms_returns_all_loaded_program_names()
    {
        WriteConfig("PMS100MI.json", ValidPms100Mi);
        var second = ValidPms100Mi.Replace("PMS100MI", "MMS200MI");
        WriteConfig("MMS200MI.json", second);
        using var svc = Build();

        var programs = svc.GetPrograms();

        programs.Should().Contain("PMS100MI").And.Contain("MMS200MI");
    }

    [Fact]
    public void Empty_directory_loads_with_zero_programs()
    {
        using var svc = Build();

        svc.GetPrograms().Should().BeEmpty();
    }

    [Fact]
    public void Invalid_json_file_is_skipped_gracefully()
    {
        WriteConfig("BAD.json", "{ this is not valid json }");
        WriteConfig("PMS100MI.json", ValidPms100Mi);
        using var svc = Build();

        // Valid config still loads; bad file is skipped
        svc.GetProgram("PMS100MI").Should().NotBeNull();
    }

    [Fact]
    public void Schema_json_is_not_loaded_as_a_program()
    {
        WriteConfig("schema.json", ValidPms100Mi); // valid structure but should be ignored
        using var svc = Build();

        svc.GetPrograms().Should().BeEmpty();
    }
}
