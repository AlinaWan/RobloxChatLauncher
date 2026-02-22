local Enums = {
    CommandType = {
        Emote = "Emote",
        Kill = "Kill",
        Notification = "Notification",
        SetTime = "SetTime",
        Announce = "Announce"
    },
    
    Target = {
        Server = "Server"
    }
}

table.freeze(Enums.CommandType)
table.freeze(Enums.Target)
table.freeze(Enums)

return Enums