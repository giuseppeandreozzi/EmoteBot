import fs from 'fs';

/**
 * Read the JSON file in which the set streamer is saved for each group
 */
export function retrieveJSON(){
    if(fs.existsSync("group.json")){
        const data = fs.readFileSync("group.json");
        
        return JSON.parse(data);
    } else
        return null;

};

/**
 * Write the JSON on a local file
 * @param {*} json JSON object to save
 */
export function saveJSON(json){
    fs.writeFileSync("group.json", JSON.stringify(json), (err) => {
        if (err) 
            return null;
    }); 
};

/**
 * Update an element of JSON object if already exist in the file
 * @param {*} jsonArray json object to update
 * @param {*} jsonElement json object's element
 */
export function updateIfExist(jsonArray, jsonElement){
    for(let el of jsonArray){
        if(el.idGroup === jsonElement.idGroup){
            el.idStreamer = jsonElement.idStreamer;
            return saveJSON(jsonArray);
        }
    }

    jsonArray.push(jsonElement);
    return saveJSON(jsonArray);
};