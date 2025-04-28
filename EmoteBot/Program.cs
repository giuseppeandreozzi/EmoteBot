using System.Net.Http.Json;
using Telegram.BotAPI;
using Telegram.BotAPI.GettingUpdates;
using Telegram.BotAPI.Stickers;
using Microsoft.Data.Sqlite;
using System.Net.Http.Headers;
using Telegram.BotAPI.AvailableTypes;
using EmoteBot;
using System.Diagnostics;
using Telegram.BotAPI.AvailableMethods;

string pathCache = Environment.GetEnvironmentVariable("EMOTEBOT_PATHCACHE");
string pathDb = Environment.GetEnvironmentVariable("EMOTEBOT_PATHDB");
string tokenBot = Environment.GetEnvironmentVariable("EMOTEBOT_TOKENBOT");
string twitchCliendId = Environment.GetEnvironmentVariable("EMOTEBOT_TWITCHCLIENTID");
string twitchClientSecret = Environment.GetEnvironmentVariable("EMOTEBOT_TWITCHSECRET");

var bot = new TelegramBotClient(tokenBot);
string botName = bot.GetMe().Username;

//Creating multiple HttpClient necessary to make request to the several REST APIs of the emote providers
var sevenTvClient = new HttpClient {
	BaseAddress = new Uri("https://7tv.io/v3/")
};

var twitchAuthClient = new HttpClient {
	BaseAddress = new Uri("https://id.twitch.tv/oauth2/token")
};

var twitchApiClient = new HttpClient {
	BaseAddress = new Uri("https://api.twitch.tv/helix/")
};

var sevenTvImageClient = new HttpClient {
	BaseAddress = new Uri("https://cdn.7tv.app/emote/")
};

var betterTTVClient = new HttpClient {
	BaseAddress = new Uri("https://api.betterttv.net/3/")
};

var betterTTVImageClient = new HttpClient {
	BaseAddress = new Uri("https://cdn.betterttv.net/emote/")
};

Dictionary<string, Dictionary<string, Emote>>? groups = new Dictionary<string, Dictionary<string, Emote>>(); //used to store the emote list of a group/user, key: chat id - value: dictionary of the emotes related to that group/user
Dictionary<string, Emote> emotes = new Dictionary<string, Emote>(); //used to store the emotes, key: emote name - value: Emote object


var chatId = "";

groups = await LoadGroups(); //load the emote list of all groups

//waiting for an update using Long Polling
var updates = bot.GetUpdates();
while (true) {
	if (updates.Any()) {
		foreach (var update in updates) {
			var msg = update?.Message?.Text;
			chatId = update?.Message?.Chat.Id.ToString();

			User? joinedGroup = update.Message?.NewChatMembers?.First();

			if (msg == null && joinedGroup == null)
				continue;

			try {
				if (msg?.Split(' ')[0] == "/setstreamer" || msg?.Split(' ')[0] == $"/setstreamer@{botName}") { // '/setstreamer <streamer name>' command
					if (msg.Split(' ').Length < 2) { //if the user did not specify the streamer name
						await bot.SendMessageAsync(chatId, "You didn't specify a streamer name. Try Again, '/help' for further help.");
					} else {
						await SetStreamerCommand(chatId.ToString(), msg.Split(' ')[1], update.Message.Chat.Title ?? update.Message.Chat.FirstName);
					}
				} else if (msg == "/help" || msg == $"/help@{botName}") { // '/help' command
					string text = "To make the bot works you should set the streamer from witch retrieve the emote set, using the command '/setstreamer <streamer name>' (i.e /setstreamer sabaku_no_sutoriimaa).";
					var reply = new ReplyParameters();
					reply.MessageId = update.Message.MessageId;
					await bot.SendMessageAsync(chatId, text, replyParameters: reply);
				} else if (msg == "/start" || msg == $"/start@{botName}") { // '/start' command
					string text = "Hello! Add me in a group and in there use the command '/setstreamer <streamer name>' to set the streamer from wich retrieve the emote set.";
					await bot.SendMessageAsync(chatId, text);
				} else if (joinedGroup != null && joinedGroup.IsBot && joinedGroup.Username == bot.GetMe().Username) { //the bot joined a group
					var text = "Hi! Now that you added me in a group, you must use the command '/setstreamer <streamer name>'.\nUse the command '/help' for further help.";
					await bot.SendMessageAsync(chatId, text);
				} else { //an user sended a message that potentially include the name of an emote
					//checking if the message is an emote's name or if the group doesn't have a streamer setted
					groups?.TryGetValue(chatId.ToString(), out emotes);
					if (emotes == null || !emotes.ContainsKey(msg))
						break;

					var reply = new ReplyParameters();
					reply.MessageId = update.Message.ReplyToMessage?.MessageId ?? update.Message.MessageId; //reply to user's message or to the message answered by the user
					var extension = (emotes[msg].animated ?? false) ? ".webm" : ".webp";

					//if the emotes is already in the cache send it to the user
					if (System.IO.File.Exists(Path.Combine(pathCache, chatId, msg + extension))) {
						Dictionary<string, InputFile> files = new Dictionary<string, InputFile>();

						bot.SendStickerAsync(chatId, new InputFile(System.IO.File.ReadAllBytes(Path.Combine(pathCache, chatId, msg + extension)), msg + extension), replyParameters: reply);
					} else { //otherwise, first download the emote and then send it to the user
						await DownloadEmoteFile(emotes[msg]);

						//if the sticker is animated convert the .gif file to a .webm file that suite the requirement for an animated sticker
						if (emotes[msg].animated ?? false) {
							await ConvertGifToSticker(msg);
						}

						bot.SendStickerAsync(chatId, new InputFile(System.IO.File.ReadAllBytes(Path.Combine(pathCache, chatId, msg + extension)), msg + extension), replyParameters: reply);
					}

				}
			} catch (BotRequestException e) {
				continue;
			}
		}
		var offset = updates.Last().UpdateId + 1;
		updates = bot.GetUpdates(offset);
	} else {
		updates = bot.GetUpdates();
	}
}

//Implements the behaviour of the /setstreamer command
async Task SetStreamerCommand(string chatId, string streamer, string name) {
	string streamerId = await GetStreamerId(streamer); //get the twitch id of the streamer

	if (streamerId == null) { //checking if the streamer exists
		await bot.SendMessageAsync(chatId, "Streamer doesn't found.");
		return;
	}

	using (var connection = new SqliteConnection($"Data Source='{pathDb}'")) {
		connection.Open();

		var command = connection.CreateCommand();
		command.CommandText =
		@"
        SELECT *
        FROM groups
        WHERE chat_id = $chat_id
    ";
		command.Parameters.AddWithValue("$chat_id", chatId);

		var commandEdit = connection.CreateCommand();
		using (var reader = command.ExecuteReader()) {
			//if the groups has already setted an other streamer, the command will be an UPDATE
			if (reader.HasRows) {
				commandEdit.CommandText = @"
						UPDATE groups
						SET streamer_id = $streamer_id
						WHERE chat_id = $chat_id
					";
			} else { //if the groups doesn't have a streamer setted, the command will be an INSERT
				commandEdit.CommandText = @"
						INSERT INTO groups
						VALUES($chat_id, $streamer_id, $name)
					";
			}

			commandEdit.Parameters.AddWithValue("chat_id", chatId);
			commandEdit.Parameters.AddWithValue("streamer_id", streamerId);
			commandEdit.Parameters.AddWithValue("name", name);

			commandEdit.ExecuteNonQuery();
		}

		Dictionary<string, Emote> emotes = new();
		if (groups.TryGetValue(chatId, out emotes)) {
			groups[chatId] = await loadEmotes(streamerId);
		} else {
			groups.Add(chatId, await loadEmotes(streamerId));
		}

		if (groups[chatId] == null) { //if the reload didn't succeeded delete the group's entry from the db and warns the user
			var commandDelete = connection.CreateCommand();
			commandDelete.CommandText = @"DELETE FROM groups WHERE chat_id = $chat_id";
			commandDelete.Parameters.AddWithValue("chat_id", chatId);
			commandDelete.ExecuteNonQuery();

			await bot.SendMessageAsync(chatId, "Streamer is not valid.");
			return;
		}
	}

	Directory.Delete(Path.Combine(pathCache, chatId), true);
	await bot.SendMessageAsync(chatId, "Streamer found.");
}

//load the emotes for a group
async Task<Dictionary<string, Emote>> loadEmotes(string streamerId) {
	Dictionary<string, Emote> emotes = new Dictionary<string, Emote>();

	bool sevenTvSucceeded = await LoadSevenTvEmotes(streamerId, emotes);
	bool bttvSucceeded = await LoadBetterTTVEmotes(streamerId, emotes);

	if (!sevenTvSucceeded || !bttvSucceeded)
		return null;
	else
		return emotes;
}

//load the emotes from 7TV
async Task<bool> LoadSevenTvEmotes(string streamerId, Dictionary<string, Emote> emotes) {
	User7TV? users;
	EmoteSet? emoteGlobals;

	try {
		users = await sevenTvClient.GetFromJsonAsync<User7TV>("users/twitch/" + streamerId);
		emoteGlobals = await sevenTvClient.GetFromJsonAsync<EmoteSet>("emote-sets/659d9aafece78f19845eb324");
	} catch (HttpRequestException e) { //the exception happens if the streamer is not valid
		return false;
	}


	foreach (EmoteSevenTv emoteSevenTv in users.emote_set.emotes) {
		if (emotes.ContainsKey(emoteSevenTv.name))
			continue;

		string format = "";
		format = (emoteSevenTv.data.animated ?? false) ? "gif" : "webp";
		Emote emote = new Emote(emoteSevenTv.id, emoteSevenTv.name, "https:" + emoteSevenTv.host?.url + "/4x." + format, emoteSevenTv.data.animated ?? false, format, "7tv");
		emotes.Add(emote.name, emote);
	}


	foreach (EmoteSevenTv emoteSevenTv in emoteGlobals.emotes) {
		if (emotes.ContainsKey(emoteSevenTv.name))
			continue;
		string format = "";
		format = (emoteSevenTv.data.animated ?? false) ? "gif" : "webp";
		Emote emote = new Emote(emoteSevenTv.id, emoteSevenTv.name, "https:" + emoteSevenTv.host?.url + "/4x." + format, emoteSevenTv.data.animated ?? false, format, "7tv");
		emotes.Add(emote.name, emote);
	}

	return true;
}

//load the emotes from BetterTTV
async Task<bool> LoadBetterTTVEmotes(string streamerId, Dictionary<string, Emote> emotes) {
	UserBTTV? user;
	List<EmoteBTTV>? globalEmotes;

	try {
		user = await betterTTVClient.GetFromJsonAsync<UserBTTV>("cached/users/twitch/" + streamerId);
		globalEmotes = await betterTTVClient.GetFromJsonAsync<List<EmoteBTTV>>("cached/emotes/global");
	} catch (HttpRequestException e) { //the exception happens if the streamer is not valid
		return false;
	}


	foreach (var emote in user.sharedEmotes) {
		if (emotes.ContainsKey(emote.code))
			continue;

		Emote emoteTmp = new Emote(emote.id, emote.code, "https://cdn.betterttv.net/emote/" + emote.id + "/3x." + emote.imageType, emote.animated, emote.imageType, "bttv");

		emotes.Add(emoteTmp.name, emoteTmp);
	}

	foreach (var emote in user.channelEmotes) {
		if (emotes.ContainsKey(emote.code))
			continue;

		Emote emoteTmp = new Emote(emote.id, emote.code, "https://cdn.betterttv.net/emote/" + emote.id + "/3x." + emote.imageType, emote.animated, emote.imageType, "bttv");

		emotes.Add(emoteTmp.name, emoteTmp);
	}



	foreach (var emote in globalEmotes) {
		if (emotes.ContainsKey(emote.code))
			continue;

		Emote emoteTmp = new Emote(emote.id, emote.code, "https://cdn.betterttv.net/emote/" + emote.id + "/3x." + emote.imageType, emote.animated, emote.imageType, "bttv");

		emotes.Add(emoteTmp.name, emoteTmp);
	}

	return true;
}

//load for all group the emotes list
async Task<Dictionary<string, Dictionary<string, Emote>>> LoadGroups() {
	Dictionary<string, Dictionary<string, Emote>> dictionary = new Dictionary<string, Dictionary<string, Emote>>();
	Dictionary<string, Emote> emotes = new();

	using (var connection = new SqliteConnection($"Data Source='{pathDb}'")) {
		connection.Open();

		var command = connection.CreateCommand();
		command.CommandText =
		@"
        SELECT *
        FROM groups
    ";

		using (var reader = command.ExecuteReader()) {
			while (reader.Read()) {
				emotes = await loadEmotes((string)reader["streamer_id"]);
				if (emotes == null) {
					return null;
				}

				dictionary.Add((string)reader["chat_id"], emotes);
			}
		}
	}

	return dictionary;
}

//get twitch token necessary to use their APIs
async Task<string> GetTwitchToken() {
	Dictionary<string, string> data = new Dictionary<string, string>();
	data.Add("client_id", twitchCliendId);
	data.Add("client_secret", twitchClientSecret);
	data.Add("grant_type", "client_credentials");

	using var req = new HttpRequestMessage(HttpMethod.Post, "") {
		Content = new FormUrlEncodedContent(data)
	};

	using HttpResponseMessage response = await twitchAuthClient.SendAsync(req);

	var responseContent = await response.Content.ReadFromJsonAsync<AuthTwitch>();

	return responseContent.access_token;
}

//retrieve the streamer id from twitch
async Task<string> GetStreamerId(string name) {
	var token = await GetTwitchToken();

	twitchApiClient.DefaultRequestHeaders.Clear();
	twitchApiClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
	twitchApiClient.DefaultRequestHeaders.Add("Client-Id", twitchCliendId);
	var user = await twitchApiClient.GetFromJsonAsync<UserTwitch>("users?login=" + name);

	if (user.data.Count == 0)
		return null;
	else
		return user.data[0].id;
}

//convert the animated emote in a format suitable to the animated sticker of telegram
async Task ConvertGifToSticker(string emoteName) {
	ProcessStartInfo startInfo = new ProcessStartInfo();

	using (Process ffmpeg = new Process()) {
		ffmpeg.StartInfo.CreateNoWindow = false;
		ffmpeg.StartInfo.UseShellExecute = false;
		ffmpeg.StartInfo.FileName = "ffmpeg";
		ffmpeg.StartInfo.Arguments = $"-y -i {Path.Combine(pathCache, chatId, emoteName + ".gif")} -r 30 -t 2.99 -an -c:v libvpx-vp9 -pix_fmt yuva420p -s 512x512 -b:v 400K {Path.Combine(pathCache, chatId, emoteName + ".webm")}";
		ffmpeg.StartInfo.RedirectStandardOutput = true;
		ffmpeg.Start();
		ffmpeg.WaitForExit();

		if (ffmpeg.HasExited) {
			System.IO.File.Delete(Path.Combine(pathCache, chatId, emoteName + ".gif"));
		}
	}
}

//download the emote file and save it in the cache
async Task DownloadEmoteFile(Emote emote) {
	Directory.CreateDirectory(Path.Combine(pathCache, chatId)); //creating the subdirectory of the group, if it doesn't exist

	HttpClient? client = null;
	string fileName = "";

	//picking the right HttpClient depending on the emote provider
	if (emote.provider == "7tv") {
		client = sevenTvImageClient;
		fileName = "4x";
	} else if (emote.provider == "bttv") {
		client = betterTTVImageClient;
		fileName = "3x";
	}

	string extension = (emote.animated ?? false) ? ".gif" : ".webp";

	byte[] img = await client.GetByteArrayAsync($"{emote.id}/{fileName}{extension}");
	await System.IO.File.WriteAllBytesAsync(Path.Combine(pathCache, chatId, emote.name + extension), img);
}