#!/usr/bin/env bash
#
# Smoke test of the Tulaziva API: walks through the main flows with curl and reports what passed and what failed.
# The API must be running already (dotnet run --project src/Tulaziva.Api, Development environment, which seeds the admin).
#
# Usage:   scripts/smoke-test.sh [base-url]        (default http://localhost:5158)
# Exit:    0 when everything passed, 1 when something failed, 2 when the API is not reachable.
#
# The users it creates use the domain smoke.tulaziva.test, so scripts/clean-test-data.sql can remove them
# (and their chats) without touching real data. Its ad is deleted by the test itself, which also removes its files;
# the files of its chat are removed at the end when the storage folder is on this machine (STORAGE_DIR overrides it).
# Texts are ASCII on purpose: accents depend on the encoding of the terminal that runs curl.

BASE_URL="${1:-${BASE_URL:-http://localhost:5158}}"
ADMIN_EMAIL="${ADMIN_EMAIL:-admin@tulaziva.local}"
ADMIN_PASSWORD="${ADMIN_PASSWORD:-Admin12345}"

JSON='Content-Type: application/json'
RUN_ID="$(date +%s)$RANDOM"
WORK_DIR="$(mktemp -d)"
trap 'rm -rf "$WORK_DIR"' EXIT

# Git Bash gives /tmp/... paths that the Windows curl cannot open for -F file=@..., so use the C:/... form when it exists.
if command -v cygpath > /dev/null 2>&1; then
  WORK_DIR="$(cygpath -m "$WORK_DIR")"
fi

passed=0
failed=0

check() { # description expected actual
  if [ "$2" = "$3" ]; then
    echo "  ok    $1"
    passed=$((passed + 1))
  else
    echo "  FAIL  $1 (expected $2, got $3)"
    failed=$((failed + 1))
  fi
}

check_contains() { # description needle text
  if [[ "$3" == *"$2"* ]]; then
    echo "  ok    $1"
    passed=$((passed + 1))
  else
    echo "  FAIL  $1 (did not find '$2')"
    failed=$((failed + 1))
  fi
}

check_missing() { # description needle text
  if [[ "$3" != *"$2"* ]]; then
    echo "  ok    $1"
    passed=$((passed + 1))
  else
    echo "  FAIL  $1 (found '$2')"
    failed=$((failed + 1))
  fi
}

status() { curl -s -o /dev/null -w "%{http_code}" "$@"; }
# The first value of a string field of the JSON that comes in. (Not the last: a chat also carries the id of the other user.)
field() { grep -o "\"$1\":\"[^\"]*\"" | head -n 1 | sed -E 's/^[^:]*:"(.*)"$/\1/'; }
auth() { echo "Authorization: Bearer $1"; }

register() { # name latitude longitude -> token
  local body="{\"email\":\"$1.$RUN_ID@smoke.tulaziva.test\",\"password\":\"Password1\",\"displayName\":\"$1\""
  [ -n "$2" ] && body="$body,\"latitude\":$2,\"longitude\":$3"
  curl -s -X POST "$BASE_URL/api/auth/register" -H "$JSON" -d "$body}" | field token
}

echo "Smoke test against $BASE_URL"

if [ "$(status "$BASE_URL/swagger/v1/swagger.json")" != "200" ]; then
  echo "The API does not answer at $BASE_URL. Start it first (Development environment)."
  exit 2
fi

# ---------------------------------------------------------------- accounts
echo "Accounts"
ANA=$(register ana 19.4326 -99.1332)
LUIS=$(register luis "" "")
ADMIN=$(curl -s -X POST "$BASE_URL/api/auth/login" -H "$JSON" -d "{\"email\":\"$ADMIN_EMAIL\",\"password\":\"$ADMIN_PASSWORD\"}" | field token)

check "register returns a token" "true" "$([ -n "$ANA" ] && [[ "$ANA" != *"{"* ]] && echo true || echo false)"
check "admin can log in" "true" "$([ -n "$ADMIN" ] && [[ "$ADMIN" != *"{"* ]] && echo true || echo false)"
check "login with a wrong password is 401" "401" "$(status -X POST "$BASE_URL/api/auth/login" -H "$JSON" -d "{\"email\":\"$ADMIN_EMAIL\",\"password\":\"Wrong12345\"}")"
check "/users/me without token is 401" "401" "$(status "$BASE_URL/api/users/me")"
check "/users/me with token is 200" "200" "$(status "$BASE_URL/api/users/me" -H "$(auth "$ANA")")"
check "a body that is not JSON is 400, not 500" "400" "$(status -X POST "$BASE_URL/api/auth/login" -H "$JSON" -d '{ nope')"
check "admin endpoints: no token 401" "401" "$(status "$BASE_URL/api/admin/users")"
check "admin endpoints: regular user 403" "403" "$(status "$BASE_URL/api/admin/users" -H "$(auth "$ANA")")"
check "admin endpoints: admin 200" "200" "$(status "$BASE_URL/api/admin/users" -H "$(auth "$ADMIN")")"

# ---------------------------------------------------------------- profile photo
echo "Profile photo"
ANA_ID=$(curl -s "$BASE_URL/api/users/me" -H "$(auth "$ANA")" | field id)
printf '\x89PNG\r\n\x1a\n-smoke-test-png-padding-padding-padding' > "$WORK_DIR/me.png"
printf '\x89PNG\r\n\x1a\n-pretend-document' > "$WORK_DIR/contract.pdf"   # real image bytes, but not a .jpg/.png name
printf 'MZ\x90\x00\x03\x00\x00\x00 fake program' > "$WORK_DIR/disguised.png"

check "no photo yet: its URL is 404" "404" "$(status "$BASE_URL/api/users/$ANA_ID/photo")"
check "setting the photo without session is 401" "401" "$(status -X PUT "$BASE_URL/api/users/me/photo" -F "file=@$WORK_DIR/me.png")"
check "a PDF is refused (400)" "400" "$(status -X PUT "$BASE_URL/api/users/me/photo" -H "$(auth "$ANA")" -F "file=@$WORK_DIR/contract.pdf")"
check "a program renamed to .png is refused (400)" "400" "$(status -X PUT "$BASE_URL/api/users/me/photo" -H "$(auth "$ANA")" -F "file=@$WORK_DIR/disguised.png")"

PHOTO=$(curl -s -X PUT "$BASE_URL/api/users/me/photo" -H "$(auth "$ANA")" -F "file=@$WORK_DIR/me.png")
PHOTO_URL=$(echo "$PHOTO" | field photoUrl)
check_contains "Ana sets her profile photo" "/api/users/$ANA_ID/photo?v=" "$PHOTO"
check "the photo is public (200)" "200" "$(status "$BASE_URL$PHOTO_URL")"
check "the photo is served as a PNG" "image/png" "$(curl -s -o /dev/null -w "%{content_type}" "$BASE_URL$PHOTO_URL")"
check_contains "the photo shows in her profile" "photoUrl" "$(curl -s "$BASE_URL/api/users/me" -H "$(auth "$ANA")")"

# ---------------------------------------------------------------- languages
echo "Languages"
check_contains "categories in Spanish" "Inmuebles" "$(curl -s "$BASE_URL/api/categories" -H 'Accept-Language: es')"
check_contains "categories in English" "Real estate" "$(curl -s "$BASE_URL/api/categories" -H 'Accept-Language: en')"
check_contains "error in English" "Incorrect email or password." "$(curl -s -X POST "$BASE_URL/api/auth/login" -H "$JSON" -H 'Accept-Language: en' -d '{"email":"nobody@x.com","password":"Wrong12345"}')"

# ---------------------------------------------------------------- ads
echo "Ads"
TITLE="Bicicleta smoke $RUN_ID"
check "create ad without session is 401" "401" "$(status -X POST "$BASE_URL/api/ads" -H "$JSON" -d '{}')"
check "create invalid ad is 400" "400" "$(status -X POST "$BASE_URL/api/ads" -H "$JSON" -H "$(auth "$ANA")" -d '{"categoryId":99,"title":"a","description":"","price":-1,"currency":"XXX"}')"

AD=$(curl -s -X POST "$BASE_URL/api/ads" -H "$JSON" -H "$(auth "$ANA")" \
  -d "{\"categoryId\":1,\"title\":\"$TITLE\",\"description\":\"Almost new\",\"price\":1500.50,\"currency\":\"mxn\"}")
AD_ID=$(echo "$AD" | field id)
check_contains "the ad takes the location where its owner registered" '"latitude":19.4326' "$AD"
check_contains "the currency is stored in upper case" '"currency":"MXN"' "$AD"

printf '\xff\xd8\xff\xe0JFIF-smoke-test-jpeg-padding-padding-padding' > "$WORK_DIR/photo.jpg"
echo "this is only text renamed to .jpg" > "$WORK_DIR/fake.jpg"

IMAGE=$(curl -s -X POST "$BASE_URL/api/ads/$AD_ID/images" -H "$(auth "$ANA")" -F "file=@$WORK_DIR/photo.jpg;type=image/jpeg")
IMAGE_ID=$(echo "$IMAGE" | field id)
check_contains "upload a real image" '"contentType":"image/jpeg"' "$IMAGE"
check "upload a text file named .jpg is 400" "400" "$(status -X POST "$BASE_URL/api/ads/$AD_ID/images" -H "$(auth "$ANA")" -F "file=@$WORK_DIR/fake.jpg;type=image/jpeg")"
check "upload by another user is 403" "403" "$(status -X POST "$BASE_URL/api/ads/$AD_ID/images" -H "$(auth "$LUIS")" -F "file=@$WORK_DIR/photo.jpg")"
check "download the image without session is 200" "200" "$(status "$BASE_URL/api/ads/$AD_ID/images/$IMAGE_ID")"

EDIT='{"categoryId":1,"title":"Stolen","description":"x","price":1,"currency":"MXN"}'
check "edit by another user is 403" "403" "$(status -X PUT "$BASE_URL/api/ads/$AD_ID" -H "$JSON" -H "$(auth "$LUIS")" -d "$EDIT")"
check "delete by another user is 403" "403" "$(status -X DELETE "$BASE_URL/api/ads/$AD_ID" -H "$(auth "$LUIS")")"

# ---------------------------------------------------------------- search
echo "Search"
UPPER_TITLE=$(echo "$TITLE" | tr '[:lower:]' '[:upper:]')
NEAR=$(curl -s -G "$BASE_URL/api/ads" --data-urlencode "text=$UPPER_TITLE" -d "latitude=19.43&longitude=-99.13&radiusKm=50&minPrice=1000&maxPrice=2000")
FAR=$(curl -s -G "$BASE_URL/api/ads" --data-urlencode "text=$TITLE" -d "latitude=20.6597&longitude=-103.3496&radiusKm=50")
check_contains "finds the ad by text (any case), distance and price" "$AD_ID" "$NEAR"
check_contains "the result carries the distance" '"distanceKm":' "$NEAR"
check_missing "does not find it 460 km away with a 50 km radius" "$AD_ID" "$FAR"
check "a radius without location is 400" "400" "$(status "$BASE_URL/api/ads?radiusKm=10")"

# ---------------------------------------------------------------- chat
echo "Chat"
MARTA=$(register marta "" "")   # has nothing to do with the chat

check "starting a chat without session is 401" "401" "$(status -X POST "$BASE_URL/api/conversations" -H "$JSON" -d "{\"adId\":\"$AD_ID\"}")"
check "the owner cannot start a chat on their own ad (409)" "409" "$(status -X POST "$BASE_URL/api/conversations" -H "$JSON" -H "$(auth "$ANA")" -d "{\"adId\":\"$AD_ID\"}")"
check "a chat about an ad that does not exist is 404" "404" "$(status -X POST "$BASE_URL/api/conversations" -H "$JSON" -H "$(auth "$LUIS")" -d '{"adId":"00000000-0000-0000-0000-000000000001"}')"

CHAT=$(curl -s -X POST "$BASE_URL/api/conversations" -H "$JSON" -H "$(auth "$LUIS")" -d "{\"adId\":\"$AD_ID\"}")
CHAT_ID=$(echo "$CHAT" | field id)
check_contains "Luis opens a chat with the owner of the ad" '"displayName":"ana"' "$CHAT"
check_contains "the chat shows the photo of the other person" "\"photoUrl\":\"/api/users/$ANA_ID/photo" "$CHAT"
check "opening it again returns the same chat (200)" "200" "$(status -X POST "$BASE_URL/api/conversations" -H "$JSON" -H "$(auth "$LUIS")" -d "{\"adId\":\"$AD_ID\"}")"

check "Luis sends a text (201)" "201" "$(status -X POST "$BASE_URL/api/conversations/$CHAT_ID/messages" -H "$JSON" -H "$(auth "$LUIS")" -d '{"body":"Hello, is it still available?"}')"
check "an empty message is 400" "400" "$(status -X POST "$BASE_URL/api/conversations/$CHAT_ID/messages" -H "$JSON" -H "$(auth "$LUIS")" -d '{"body":"  "}')"
check_contains "Ana has 1 unread message" '"count":1' "$(curl -s "$BASE_URL/api/conversations/unread-count" -H "$(auth "$ANA")")"
check_contains "the chat is in the list of Ana, with its last message" "Hello, is it still available?" "$(curl -s "$BASE_URL/api/conversations" -H "$(auth "$ANA")")"
check_contains "Ana reads the messages" "Hello, is it still available?" "$(curl -s "$BASE_URL/api/conversations/$CHAT_ID/messages" -H "$(auth "$ANA")")"
check "Ana marks the chat as read (204)" "204" "$(status -X POST "$BASE_URL/api/conversations/$CHAT_ID/read" -H "$(auth "$ANA")")"
check_contains "Ana has no unread messages left" '"count":0' "$(curl -s "$BASE_URL/api/conversations/unread-count" -H "$(auth "$ANA")")"

# Privacy: the conversation belongs to its two participants. A third user and an admin get "not found".
for WHO in "Marta:$MARTA" "the admin:$ADMIN"; do
  NAME="${WHO%%:*}"; TOKEN="${WHO#*:}"
  check "$NAME cannot open the chat (404)" "404" "$(status "$BASE_URL/api/conversations/$CHAT_ID" -H "$(auth "$TOKEN")")"
  check "$NAME cannot read its messages (404)" "404" "$(status "$BASE_URL/api/conversations/$CHAT_ID/messages" -H "$(auth "$TOKEN")")"
  check "$NAME cannot write in it (404)" "404" "$(status -X POST "$BASE_URL/api/conversations/$CHAT_ID/messages" -H "$JSON" -H "$(auth "$TOKEN")" -d '{"body":"I am listening"}')"
done

# Files: the chat only carries .jpg and .png images. Documents, zip files and programs are refused, and so is anything
# that is not what its name says.
printf '%%PDF-1.7\n%s\n' "smoke test document" > "$WORK_DIR/document.pdf"
printf 'PK\x03\x04 fake zip' > "$WORK_DIR/archive.zip"
printf 'MZ\x90\x00\x03\x00\x00\x00 fake program' > "$WORK_DIR/setup.exe"
cp "$WORK_DIR/setup.exe" "$WORK_DIR/invoice.png"

CHAT_FILES="$BASE_URL/api/conversations/$CHAT_ID/messages/files"
check "a PDF is refused (400)" "400" "$(status -X POST "$CHAT_FILES" -H "$(auth "$LUIS")" -F "files=@$WORK_DIR/document.pdf")"
check "a zip file is refused (400)" "400" "$(status -X POST "$CHAT_FILES" -H "$(auth "$LUIS")" -F "files=@$WORK_DIR/archive.zip")"
check "a program (.exe) is refused (400)" "400" "$(status -X POST "$CHAT_FILES" -H "$(auth "$LUIS")" -F "files=@$WORK_DIR/setup.exe")"
check "a program renamed to .png is refused (400)" "400" "$(status -X POST "$CHAT_FILES" -H "$(auth "$LUIS")" -F "files=@$WORK_DIR/invoice.png")"

SENT=$(curl -s -X POST "$CHAT_FILES" -H "$(auth "$LUIS")" -F "body=Private words about the picture" -F "files=@$WORK_DIR/me.png")
FILE_URL=$(echo "$SENT" | field url)
check_contains "Luis sends a PNG with a text" '"contentType":"image/png"' "$SENT"
check "Ana downloads the image (200)" "200" "$(status "$BASE_URL$FILE_URL" -H "$(auth "$ANA")")"
check "the download is a PNG" "image/png" "$(curl -s -o /dev/null -w "%{content_type}" "$BASE_URL$FILE_URL" -H "$(auth "$ANA")")"
check "an admin can download the image (200)" "200" "$(status "$BASE_URL$FILE_URL" -H "$(auth "$ADMIN")")"
check "another user cannot download the image (404)" "404" "$(status "$BASE_URL$FILE_URL" -H "$(auth "$MARTA")")"
check "the image needs a session (401)" "401" "$(status "$BASE_URL$FILE_URL")"

ADMIN_FILES=$(curl -s "$BASE_URL/api/admin/attachments?pageSize=100" -H "$(auth "$ADMIN")")
check_contains "the admin list shows the image" "me.png" "$ADMIN_FILES"
check_missing "the admin list never shows the text of the chat" "Private words" "$ADMIN_FILES"
check "the admin list is not for regular users (403)" "403" "$(status "$BASE_URL/api/admin/attachments" -H "$(auth "$LUIS")")"

# ---------------------------------------------------------------- moderation and lifecycle
echo "Moderation and lifecycle"
check "a regular user cannot take an ad down (403)" "403" "$(status -X POST "$BASE_URL/api/admin/ads/$AD_ID/remove" -H "$(auth "$ANA")")"
check "an admin takes the ad down (204)" "204" "$(status -X POST "$BASE_URL/api/admin/ads/$AD_ID/remove" -H "$(auth "$ADMIN")")"
check "the public no longer sees it (404)" "404" "$(status "$BASE_URL/api/ads/$AD_ID")"
check "its owner still sees it (200)" "200" "$(status "$BASE_URL/api/ads/$AD_ID" -H "$(auth "$ANA")")"
check "an admin still sees it (200)" "200" "$(status "$BASE_URL/api/ads/$AD_ID" -H "$(auth "$ADMIN")")"
check_missing "it is not in the public search" "$AD_ID" "$(curl -s -G "$BASE_URL/api/ads" --data-urlencode "text=$TITLE")"
check_contains "it is still in the list of its owner" "$AD_ID" "$(curl -s "$BASE_URL/api/ads/mine" -H "$(auth "$ANA")")"
check "renewing a removed ad is 409" "409" "$(status -X POST "$BASE_URL/api/ads/$AD_ID/renew" -H "$(auth "$ANA")")"
check "the owner deletes the ad (204)" "204" "$(status -X DELETE "$BASE_URL/api/ads/$AD_ID" -H "$(auth "$ANA")")"
check "the ad is gone (404)" "404" "$(status "$BASE_URL/api/ads/$AD_ID" -H "$(auth "$ANA")")"
check "its image is gone (404)" "404" "$(status "$BASE_URL/api/ads/$AD_ID/images/$IMAGE_ID" -H "$(auth "$ANA")")"

# ---------------------------------------------------------------- profile photo, removal
echo "Profile photo, removal"
check "Ana removes her photo (200)" "200" "$(status -X DELETE "$BASE_URL/api/users/me/photo" -H "$(auth "$ANA")")"
check "the photo is gone (404)" "404" "$(status "$BASE_URL$PHOTO_URL")"

# The chat is not deleted through the API (it belongs to its two users), so its files are removed here when the storage is on this machine.
# The same goes for a photo that is still there because a check above failed.
STORAGE_DIR="${STORAGE_DIR:-$(cd "$(dirname "$0")/.." && pwd)/src/Tulaziva.Api/storage}"
if [ -n "$CHAT_ID" ] && [ -d "$STORAGE_DIR/chat/${CHAT_ID//-/}" ]; then
  rm -rf "$STORAGE_DIR/chat/${CHAT_ID//-/}"
fi
if [ -n "$ANA_ID" ] && [ -d "$STORAGE_DIR/profiles/${ANA_ID//-/}" ]; then
  rm -rf "$STORAGE_DIR/profiles/${ANA_ID//-/}"
fi

# ---------------------------------------------------------------- result
echo
echo "$passed passed, $failed failed"
echo "Test users (@smoke.tulaziva.test) stay in the database; remove them with scripts/clean-test-data.sql"

[ "$failed" -eq 0 ]
