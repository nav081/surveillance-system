# Monitoring Engine

Independent Python service that reads camera RTSP streams, detects faces with
YOLOv11, tracks them with ByteTrack, recognizes enrolled people with InsightFace,
and reports entry and exit events to the Admin Portal API.

## Pipeline

1. `service.py` loads models and enrolled embeddings, then starts one `CameraWorker`
   per active camera assigned to this shard.
2. Each worker reads frames over RTSP (`cv2.VideoCapture`, FFmpeg backend), with
   reconnect backoff.
3. Every `FRAME_STRIDE`-th frame runs YOLOv11 person detection (COCO class 0), then
   ByteTrack. Person boxes drive tracking and line crossings, so people are counted
   even when their face is not visible.
4. Identity lookup runs inside each tracked person's box. A YOLOv11 face detector
   searches that crop, the largest face is embedded with InsightFace, and the
   embedding is matched within the tenant. A match gives `person:{id}`. Otherwise
   the track stays `track:{cameraId}:{trackId}`. An attempt counts only when a face
   was found, so people who turn away are retried later.
5. `LineCounter` turns crossings of the camera's counting line into entry or exit
   events. Crossings need `LINE_DEBOUNCE_FRAMES` consecutive frames beyond the
   `LINE_HYSTERESIS` band.
6. `EventSink` batches events to `POST /api/internal/attendance/events`. An exit
   reuses the presence key stored at entry.

## Face weights (required)

The engine uses two YOLOv11 models, both mounted from `./models`:

- **Person model** (`PERSON_WEIGHTS`, default `/models/yolo11n.pt`): stock Ultralytics
  COCO weights. Used for tracking and entry/exit lines.
- **Face model** (`YOLO_WEIGHTS`): a face-trained YOLOv11 model, for example
  `yolov11n-face.pt` (WIDERFace). It is used only inside person boxes. Its license
  is typically non-commercial, so review it before commercial use. This project
  does not ship weights.

Camera RTSP URLs must use the lowercase `rtsp://` scheme. The engine forces TCP
transport (`OPENCV_FFMPEG_CAPTURE_OPTIONS`), which Docker bridge networking needs.

InsightFace model packs (default `buffalo_l`) download on first use into
`~/.insightface`. Mount that path as a volume so restarts do not re-download.

## Configuration

| Variable | Default | Purpose |
|---|---|---|
| `API_BASE_URL` | required | Admin Portal base URL, e.g. `http://api:8080` |
| `SERVICE_API_KEY` | required | Value of the server's internal service key |
| `YOLO_WEIGHTS` | `/models/yolo11n-face.pt` | Face detection weights (run inside person boxes) |
| `YOLO_FACE_CLASS_ID` | `0` | Class index for faces in those weights |
| `PERSON_WEIGHTS` | `/models/yolo11n.pt` | Person detection weights (COCO, class 0) |
| `PERSON_CONFIDENCE` | `0.35` | Minimum person confidence for tracking |
| `PERSON_IMGSZ` | `960` | Inference size for person detection |
| `DETECT_IMGSZ` | `1280` | Inference size for face detection (small faces need a large size) |
| `INSIGHTFACE_MODEL` | `buffalo_l` | InsightFace model pack |
| `INSIGHTFACE_DET_SIZE` | `320` | Detector input size for recognition crops |
| `DETECT_CONFIDENCE` | `0.4` | Minimum YOLO face confidence |
| `INFERENCE_DEVICE` | `cpu` | Device for YOLO inference (e.g. `cpu`, `cuda:0`) |
| `FRAME_STRIDE` | `3` | Process every Nth frame |
| `IDENTIFY_EVERY_N_FRAMES` | `10` | Frames between identity attempts for a track |
| `MAX_IDENTIFY_ATTEMPTS` | `3` | Identity attempts per track before giving up |
| `FACE_MATCH_THRESHOLD` | `0.45` | Cosine similarity needed to identify a person (0 to 1) |
| `LINE_DEBOUNCE_FRAMES` | `3` | Consecutive frames needed to commit a crossing |
| `LINE_HYSTERESIS` | `0.02` | Dead band around the counting line |
| `EVENT_BATCH_SIZE` | `50` | Max events per ingest request |
| `EVENT_FLUSH_INTERVAL_S` | `2` | Max seconds before a partial batch is sent |
| `CAMERA_REFRESH_S` | `60` | How often the camera list is re-read |
| `EMBEDDING_REFRESH_S` | `300` | How often enrolled embeddings are re-read |
| `HEARTBEAT_S` | `30` | Camera heartbeat interval |
| `RECONNECT_MAX_S` | `60` | Max backoff between RTSP reconnect attempts |
| `SNAPSHOTS_ENABLED` | `true` | Upload face-crop snapshots with entry and exit events |
| `ENGINE_SHARD_COUNT` | `1` | Number of engine replicas |
| `ENGINE_SHARD_INDEX` | `0` | This replica's index (0 to count-1) |

Cameras are assigned to shards with `crc32(cameraId) % ENGINE_SHARD_COUNT`.

## Enrolling people

Enrollment sends only a face embedding, never the image itself.

```bash
export API_BASE_URL=http://localhost:8080
export ENROLL_PASSWORD=...   # or omit to be prompted
python enroll.py --api "$API_BASE_URL" --email admin@example.com --name "Jane Doe" --image path/to/photo.jpg
```

The photo must contain exactly one face. The command logs in as an admin of the
tenant and calls `POST /api/persons`.

## Running

Docker (from the repo root, via compose):

```bash
docker compose up -d monitoring
```

Local:

```bash
pip install -r requirements.txt
SERVICE_API_KEY=... API_BASE_URL=http://localhost:8080 python -m engine
```

## Tests

```bash
pip install -r requirements-dev.txt
python -m pytest tests
```

The tests cover the pure logic (line counter, embedding matcher, configuration).
They do not load the ML models or a camera.

## Scaling

Models are shared within one process and inference is serialized by a lock, so
one process does not parallelize across threads. Scale by running several replicas
with `ENGINE_SHARD_COUNT=N` and distinct `ENGINE_SHARD_INDEX` values. Each replica
handles a disjoint set of cameras. Hundreds of cameras need GPU capacity; set
frame stride and the model size to match your hardware.

## Known limitations

- Track IDs reset when the engine restarts. A person already inside may be counted
  as a new track, so an entry can appear without a prior exit being recorded.
- If a track disappears mid-crossing, the server keeps the open presence. The
  server never auto-closes presences, so an exit can be missing until it is
  corrected manually.
- Anonymous presences (not identified) do not follow a person across cameras in
  the same home.
- If event ingest keeps failing after retries, that batch is dropped and logged.
- Recognition runs InsightFace's detector on an expanded face crop rather than a
  landmark-aligned crop. Accuracy will be lower than with aligned input.
- Ultralytics, supervision, and InsightFace APIs are used as documented but have
  not been validated end-to-end against live camera streams in this repository.
  Test with real footage before relying on counts.
- Only the face embedding and a face-crop snapshot leave the engine. RTSP URLs are
  never logged.
