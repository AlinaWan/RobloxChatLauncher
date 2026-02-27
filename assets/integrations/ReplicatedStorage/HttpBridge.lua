--!language luau
--[=[
    Copyright (c) 2026 Riri a.k.a. Alina Wan <https://github.com/AlinaWan>

    Source: https://github.com/AlinaWan/RobloxChatLauncher/blob/main/assets/integrations/
    
    Licensed under the MPL 2.0 license.
    See https://www.mozilla.org/en-US/MPL/2.0/ for full text.
--]=]
local HttpService = game:GetService("HttpService")
local HttpBridge = {}

-------------------------------
-- Configuration
-------------------------------
local BASE_URL = "https://RobloxChatLauncher.onrender.com"
local API_KEY = HttpService:GetSecret("RCL_API_KEY") -- Replace with the key given to you by Riri or RCL admin
local UNIVERSE_ID = tostring(game.GameId) -- game.GameId is the UniverseId. The game must be published or game.GameId will return 0 and requests will fail with 403

-- Helper to format URLs consistently
local function formatUrl(path: string): string
    if path:sub(1, 4) == "http" then return path end
    if path:sub(1, 1) ~= "/" then path = "/" .. path end
    return BASE_URL .. path
end

-------------------------------
-- Sender (Egress)
-------------------------------
function HttpBridge.send(url: string, payload: table)
    local fullUrl = formatUrl(url)
    task.spawn(function()
        local success, result = pcall(function()
            return HttpService:RequestAsync({
                Url = fullUrl,
                Method = "POST",
                Headers = {
                    ["x-api-key"] = API_KEY,
                    ["x-universe-id"] = UNIVERSE_ID,
                    ["x-job-id"] = game.JobId,
                    ["Content-Type"] = "application/json"
                },
                Body = HttpService:JSONEncode(payload)
            })
        end)
        if not success then warn("[RCL Egress] Error:", result) end
    end)
end

-------------------------------
-- Listener (Ingress)
-------------------------------
local handlers = {} 
local isPolling = false
local DEFAULT_POLL_INTERVAL = 1.0

function HttpBridge.registerHandler(handler: (any) -> ())
    table.insert(handlers, handler)
    
    if not isPolling then
        isPolling = true
        HttpBridge._startCentralLoop("/api/v1/commands", DEFAULT_POLL_INTERVAL)
    end
end

function HttpBridge._startCentralLoop(endpoint: string, interval: number)
    local fullUrl = formatUrl(endpoint)
    
    -- Request Headers for Authentication
    local headers = {
        ["x-api-key"] = API_KEY,
        ["x-universe-id"] = UNIVERSE_ID,
        ["x-job-id"] = game.JobId
    }

    task.spawn(function()
        while true do
            local success, response = pcall(function() 
                -- We use RequestAsync to send custom headers with a GET request
                return HttpService:RequestAsync({
                    Url = fullUrl,
                    Method = "GET",
                    Headers = headers
                })
            end)
            
            if success and response.Success then
                local body = response.Body
                if #body > 2 then
                    local ok, decodedData = pcall(HttpService.JSONDecode, HttpService, body)
                    if ok then
                        for _, handlerFunc in ipairs(handlers) do
                            task.spawn(handlerFunc, decodedData)
                        end
                    end
                end
            elseif not success then
                warn("[RCL Ingress] Connection error:", response)
            elseif response.StatusCode == 403 then
                warn("[RCL Ingress] Auth Failed: Check API Key and UniverseID")
            end
            
            task.wait(interval)
        end
    end)
end

return HttpBridge
