import { Telegraf, Input } from 'telegraf';
import { message } from 'telegraf/filters';
import { XMLHttpRequest } from "xmlhttprequest";
import fetch from 'node-fetch';
import { existsSync, unlinkSync} from 'fs';
import HashMap from 'hashmap';
import { image } from 'image-downloader';
import sharp from 'sharp';
import { exec } from "child_process";
import "dotenv/config";

import TokenTwitch from "./tokenTwitch.js";
import { retrieveJSON, updateIfExist }  from "./localJSON.js";

var jsonArray = retrieveJSON();
var hash = {};
hash = createHashJSON();

var token;

TokenTwitch().then(data => {
    token = data.access_token;
});

if(jsonArray == null){
    jsonArray = [];
}

const bot = new Telegraf(process.env.API_TELEGRAM);

bot.start((ctx) => ctx.reply("Add me in a group and use the command /setstreamer [streamer's name] to set the streamer from which you would like to retrieve the emotes"));

bot.command('refresh', ctx => {
    ctx.telegram.getChatMember(ctx.message.chat.id, ctx.message.from.id).then(async function(data) {
        if ((data.status == "creator") || (data.status == "administrator")){
            hash = createHashJSON();
            ctx.reply("Refresh complete");
        }
    });
});

bot.command('help', ctx => {
    let id = (ctx.message.reply_to_message) ? ctx.message.reply_to_message.message_id : ctx.message.message_id;

    ctx.reply("To make the bot work you must set the streamer from which you would like to retrieve the emotes with /setstreamer [streamer's name]", {reply_to_message_id: id});
});

bot.command('setstreamer', ctx => {
    let id = (ctx.message.reply_to_message) ? ctx.message.reply_to_message.message_id : ctx.message.message_id;

    ctx.telegram.getChatMember(ctx.message.chat.id, ctx.message.from.id).then(async function(data) {
        if ((data.status == "creator") || (data.status == "administrator")){
            let user = ctx.message.text.substring(ctx.message.text.indexOf(" ") + 1);

            fetch("https://api.twitch.tv/helix/users?login=" + user, {
                headers: {
                    'Client-ID': process.env.CLIENT_ID,
                    'Authorization': "Bearer " + token
                }
            }).then(data => {
                data.json().then(data => {
                    if(data.data[0]){ //data.data[0] contains the streamer's information
                        updateIfExist(jsonArray, {idGroup: ctx.chat.id, idStreamer: data.data[0].id, groupName: ctx.message.chat.title});
                        jsonArray = retrieveJSON();
                        hash = createHashJSON();
                        ctx.reply("Streamer found", {reply_to_message_id: id});
                    }else
                        ctx.reply("Streamer not found", {reply_to_message_id: id});
                });
            });

        }
    });
});
    
bot.on(message('text'), ctx => {
    var text=ctx.update.message.text
    let emotes = hash.get(ctx.chat.id);
    let emote = (emotes) ? emotes.get(text) : null;

    if (emote) {
        let partialPath = process.cwd() + "/temp/" + text; //path where to save the emotes
        var request = new XMLHttpRequest();
        request.open("GET", emote.url + ".gif"); //.gif to check if it's an animated emote
        request.send();
        request.onload = async function() {
            let id = (ctx.message.reply_to_message) ? ctx.message.reply_to_message.message_id : ctx.message.message_id;
            if ((emote.type == null && request.status == 200)){ //7tv's emote animated
                if (!existsSync(partialPath + ".webm") ) {
                    let options = {
                        url: emote.url + ".gif",
                        dest:  partialPath + ".gif"
                    };
                          
                image(options).then(({ filename }) => {
                                ctx.replyWithDocument({ source: partialPath + ".gif"}, {reply_to_message_id: id});
                            }).catch((err) => console.error(err));
                      
                        }else
                            ctx.replyWithDocument({ source: partialPath + ".gif"}, {reply_to_message_id: id});
            }
            else if (emote.type === "png" || emote.type === "ffz"){ //ffz's emote
                if (!existsSync(partialPath + ".webp") ) {
                    let options = {
                        url: emote.url,
                        dest:  partialPath + ".png"
                    };
                          
                    image(options).then(({ filename }) => {
                        sharp(partialPath + ".png").webp({ lossless: true })
                        .toFile(partialPath + ".webp").then( data => { 
                            ctx.replyWithSticker({ source: partialPath + ".webp"}, {reply_to_message_id: id}).then((response) => {
                                    unlinkSync(partialPath + ".png");})
                            .catch( err => { console.error(err) });
                                    
                        });

                    }).catch((err) => console.error(err));
                }else
                    ctx.replyWithSticker({ source: partialPath + ".webp"}, {reply_to_message_id: id});
                    
            } else if (emote.type === "gif"){ //bttv's emote animated
                if (!existsSync(partialPath + ".gif")) {
                  let options = {
                    url: emote.url,
                    dest:  partialPath + ".gif"
                  };
                        
                  image(options).then(({ filename }) => {    
                    ctx.replyWithDocument({ source: partialPath + ".gif"}, {reply_to_message_id: id});
                    }).catch( err => { console.error(err) });
                }else
                  ctx.replyWithDocument({ source: partialPath + ".gif"}, {reply_to_message_id: id});

            }
            else{
                if (!existsSync(partialPath + ".webp") ) {
                    let options = {
                        url: emote.url + ".webp",
                        dest:  partialPath + ".webp"
                    };
                          
                    image(options).then(({ filename }) => {
                        ctx.replyWithSticker({ source: partialPath + ".webp"}, {reply_to_message_id: id});
                    }).catch((err) => console.error(err));
                }else
                    ctx.replyWithSticker({ source: partialPath + ".webp"}, {reply_to_message_id: id});
            }
        }
            
    }
});

bot.launch();


//utility functions
/**
 * create the hashmap of a given streamer used for retrieve the emote to send in the telegram's chat
 * @param {*} idStreamer streamer's id 
 * @returns hashmap
 */
function setHashMap(idStreamer){
    var jsonArr = [];
    var hash2 = new HashMap();

    retrieveJSON7TV(idStreamer).then((function(data) {
        var emotes = data.emote_set.emotes;
        emotes.forEach((el) => {
            let json = {
                "id": el.id,
                "name": el.name,
            };
  
            jsonArr.push(json);

            jsonArr.forEach((el) => {

                hash2.set(el.name,{ "url":'https://cdn.7tv.app/emote/' + el.id + '/4x',  "type": null });
            });
        });

    })).catch(err => {
        console.log(err);

    });
    
    jsonArr = [];
    retrieveJSON7TVGlobal().then((function(result) {
    var emotes = result.emotes;
    emotes.forEach((el) => {
      let json = {
        "id": el.id,
        "name": el.name,
      };
            
      jsonArr.push(json);

      jsonArr.forEach((el) => {
        hash2.set(el.name,{ "url":'https://cdn.7tv.app/emote/' + el.id + '/4x',  "type": null });
      });
      });

    })).catch(err => {
        console.log(err);

    });
    
    jsonArr = [];
    retrieveJSONBTTV(idStreamer).then((function(result) {
        var emotes = result.channelEmotes;

        emotes.forEach((el) => {
            hash2.set(el.code,{ "url": 'https://cdn.betterttv.net/emote/' + el.id + '/3x',  "type": el.imageType });
        });

        var emotes = result.sharedEmotes;
        emotes.forEach((el) => {
            hash2.set(el.code, { "url": 'https://cdn.betterttv.net/emote/' + el.id + '/3x',  "type": el.imageType });
        });

    })).catch(err => {
        console.log(err);

    });

    jsonArr = [];
    retrieveJSONBTTVGlobal().then((function(result) {

        result.forEach((el) => {
            hash2.set(el.code,{ "url": 'https://cdn.betterttv.net/emote/' + el.id + '/3x',  "type": el.imageType });
        });

    })).catch(err => {
        console.log(err);

    });

    jsonArr = [];
    retrieveJSONFFZ(idStreamer).then((function(result) {
        if(result.room){
            let id = result.room.set;
            var emotes = result.sets[id].emoticons;
            emotes.forEach((el) => {
                hash2.set(el.name,{ "url": 'https://cdn.frankerfacez.com/emote/' + el.id + '/2',  "type": "ffz" });
            });
        }
    })).catch(err => {
        console.log(err);
    });

    jsonArr = [];
    retrieveJSONFFZGlobal().then((function(result) {
        var id = result.default_sets;
        id.forEach((id) => {
            let emoticons = result.sets[id].emoticons;
            emoticons.forEach((el) => {
                hash2.set(el.name,{ "url": 'https://cdn.frankerfacez.com/emote/' + el.id + '/2',  "type": "ffz" });
            });
        });

    })).catch(err => {
        console.log(err);

    });

    return hash2;
}
/**
 * retrieve the streamer's 7tv emotes
 * @param {*} idStreamer streamer's id
 * @returns json array of 7tv emotes
 */
async function retrieveJSON7TV(idStreamer) {
    const user = await fetch('https://7tv.io/v3/users/twitch/' + idStreamer);
    const response = await fetch('https://7tv.io/v3/emote-sets/60e31efc78cfe95e2de215b5');
    return user.json();
}

/**
 * retrieve the 7tv global emotes
 * @returns json array of 7tv global emotes
 */
async function retrieveJSON7TVGlobal() {
    const response = await fetch('https://7tv.io/v3/emote-sets/62cdd34e72a832540de95857');
    return await response.json();
}

/**
 * retrieve the streamer's BetterTTV emotes
 * @param {*} idStreamer streamer's id
 * @returns json array of BetterTTV emotes
 */
async function retrieveJSONBTTV(idStreamer) {
    const response = await fetch('https://api.betterttv.net/3/cached/users/twitch/' + idStreamer);
    return await response.json();
}

/**
 * retrieve the BetterTTV global emotes
 * @returns json array of BetterTTV global emotes
 */
async function retrieveJSONBTTVGlobal() {
    const response = await fetch('https://api.betterttv.net/3/cached/emotes/global');
    return await response.json();
}

/**
 * retrieve the streamer's FrankerFaceZ emotes
 * @param {*} idStreamer streamer's id
 * @returns json array of FrankerFaceZ emotes
 */
async function retrieveJSONFFZ(idStreamer) {
    const response = await fetch('https://api.frankerfacez.com/v1/room/id/' + idStreamer);
    return await response.json();
}

/**
 * retrieve the FrankerFaceZ global emotes
 * @returns json array of FrankerFaceZ global emotes
 */
async function retrieveJSONFFZGlobal() {
    const response = await fetch('https://api.frankerfacez.com/v1/set/global/ids');
    return await response.json();
}

/**
 * create the hasmap that contains hashmaps of emotes for each telegram's group
 * @returns hashmap 
 */
function createHashJSON() {
    if(!jsonArray)
        return {};
    const tempHash = new HashMap();
    console.log("Building Hashmap");

    for(const el of jsonArray){
        let data = setHashMap(el.idStreamer);
        tempHash.set(el.idGroup, data);
    }

    
    console.log("Hashmap builded");
    return tempHash;
}
