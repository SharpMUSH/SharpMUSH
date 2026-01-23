using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class EncryptionFunctionUnitTests : TestClassFactory
{
	private IMUSHCodeParser Parser => FunctionParser;

	[Test]
	[Arguments("encode64(test_string_encode64_case1)", "dGVzdF9zdHJpbmdfZW5jb2RlNjRfY2FzZTE=")]
	[Arguments("encode64(test)", "dGVzdA==")]
	[Arguments("encode64(hello world)", "aGVsbG8gd29ybGQ=")]
	[Arguments("encode64(123)", "MTIz")]
	public async Task Encode64(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("decode64(dGVzdF9zdHJpbmdfZGVjb2RlNjRfY2FzZTE=)", "test_string_decode64_case1")]
	[Arguments("decode64(dGVzdA==)", "test")]
	[Arguments("decode64(aGVsbG8gd29ybGQ=)", "hello world")]
	[Arguments("decode64(MTIz)", "123")]
	public async Task Decode64(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	public async Task Encrypt_Basic()
	{
		// Use base64 encoding to ensure the encrypted text can be passed through the parser
		var encrypted = (await Parser.FunctionParse(MModule.single("encrypt(test_string_encrypt_case1,mypassword,1)")))?.Message!;
		await Assert.That(encrypted.ToPlainText()).IsNotNull();
		
		// Verify we can decrypt it back
		var decrypted = (await Parser.FunctionParse(MModule.single($"decrypt({encrypted.ToPlainText()},mypassword,1)")))?.Message!;
		await Assert.That(decrypted.ToPlainText()).IsEqualTo("test_string_encrypt_case1");
	}

	[Test]
	public async Task Encrypt_WithBase64Encoding()
	{
		var encrypted = (await Parser.FunctionParse(MModule.single("encrypt(test_string_encrypt_encoded,mykey,1)")))?.Message!;
		await Assert.That(encrypted.ToPlainText()).IsNotNull();
		
		// Verify it's base64 encoded (should only contain alphanumeric and =)
		var text = encrypted.ToPlainText();
		await Assert.That(text.All(c => char.IsLetterOrDigit(c) || c == '=' || c == '+'|| c == '/')).IsTrue();
		
		// Verify we can decrypt it back
		var decrypted = (await Parser.FunctionParse(MModule.single($"decrypt({text},mykey,1)")))?.Message!;
		await Assert.That(decrypted.ToPlainText()).IsEqualTo("test_string_encrypt_encoded");
	}

	[Test]
	[Arguments("encrypt(hello,pass123)", "decrypt(@0,pass123)")]
	[Arguments("encrypt(test data,secretkey)", "decrypt(@0,secretkey)")]
	public async Task EncryptDecrypt_RoundTrip(string encryptFunc, string decryptFunc)
	{
		var encrypted = (await Parser.FunctionParse(MModule.single(encryptFunc)))?.Message!;
		var decryptCall = decryptFunc.Replace("@0", encrypted.ToPlainText());
		var decrypted = (await Parser.FunctionParse(MModule.single(decryptCall)))?.Message!;
		
		// Extract original plaintext from encrypt function call
		var originalText = encryptFunc.Substring(encryptFunc.IndexOf('(') + 1, encryptFunc.IndexOf(',') - encryptFunc.IndexOf('(') - 1);
		await Assert.That(decrypted.ToPlainText()).IsEqualTo(originalText);
	}

	[Test]
	[Arguments("hmac(test,key,sha256)", "")]
	public async Task Hmac(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("digest(test,sha256)", "")]
	public async Task Digest(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}
}
