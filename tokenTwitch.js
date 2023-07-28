import "dotenv/config";

/**
 * Retrieve the Twitch API's token
 * @returns Twitch API's token
 */
export default async function getTokenTwitch() {
     const body = {
         client_id: process.env.CLIENT_ID,
         client_secret: process.env.CLIENT_SECRET,
         grant_type: "client_credentials"
     };
     
     const data = await fetch("https://id.twitch.tv/oauth2/token", {
         method: "POST",
         headers:{ "Content-Type": "application/json" },
         body: JSON.stringify(body)
     });
     return await data.json();
 }