/* This file contains the several models necessaries to call the REST APIs
 */

namespace EmoteBot {

	public record class Emote(
		string? id = null,
		string? name = null,
		string? url = null,
		bool? animated = false,
		string? format = null,
		string? provider = null
		);

	public record class UserBTTV(
		string? id = null,
		List<EmoteBTTV>? channelEmotes = null,
		List<EmoteBTTV>? sharedEmotes = null
		);

	public record class EmoteBTTV(
		string? id = null,
		string? code = null,
		string? imageType = null,
		bool? animated = null
		);

	public record class User7TV(
		long? id = null,
		EmoteSet? emote_set = null
		);

	public record class EmoteSet(
		string? id = null,
		List<EmoteSevenTv>? emotes = null
		);

	public record class EmoteSevenTv(
		string? id = null,
		string? name = null,
		Host? host = null,
		Data? data = null
		);

	public record class Host(
		string? url = null
		);
	public record class Data(
		bool? animated = null
		);

	public record class UserTwitch(
		List<DataTwitch>? data = null
		);

	public record class DataTwitch(
		string? id = null,
		string? login = null
		);

	public record class AuthTwitch(
		string? access_token = null
		);
}
