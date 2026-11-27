"""Enrolls a person from a face image by calling the Admin Portal API.

Usage:
  python enroll.py --api http://localhost:8080 --email admin@acme.test --name "Jane Doe" --image jane.jpg

The password is read from the ENROLL_PASSWORD environment variable, or prompted for if unset.
Only the 512-d normalized embedding is sent, not the image.
"""

from __future__ import annotations

import argparse
import getpass
import os
import sys

import cv2
import httpx
from insightface.app import FaceAnalysis


def _embed(image_path: str, model_name: str) -> list[float]:
    image = cv2.imread(image_path)
    if image is None:
        raise SystemExit(f"Could not read image: {image_path}")

    app = FaceAnalysis(name=model_name, providers=["CPUExecutionProvider"])
    app.prepare(ctx_id=-1, det_size=(640, 640))
    faces = app.get(image)
    if len(faces) != 1:
        raise SystemExit(f"Expected exactly one face in the image, found {len(faces)}")
    return [float(x) for x in faces[0].normed_embedding]


def main() -> None:
    parser = argparse.ArgumentParser(description="Enroll a person for face recognition.")
    parser.add_argument("--api", required=True, help="Admin Portal base URL")
    parser.add_argument("--email", required=True, help="Admin user email")
    parser.add_argument("--name", required=True, help="Display name for the person")
    parser.add_argument("--image", required=True, help="Path to a clear, front-facing photo")
    parser.add_argument("--model", default="buffalo_l", help="InsightFace model pack")
    args = parser.parse_args()

    password = os.environ.get("ENROLL_PASSWORD") or getpass.getpass("Password: ")
    embedding = _embed(args.image, args.model)

    with httpx.Client(base_url=args.api.rstrip("/"), timeout=30.0) as client:
        login = client.post("/api/auth/login", json={"email": args.email, "password": password})
        if login.status_code != 200:
            sys.exit(f"Login failed: {login.status_code}")
        token = login.json()["accessToken"]

        response = client.post(
            "/api/persons",
            headers={"Authorization": "Bearer " + token},
            json={"displayName": args.name, "embedding": embedding},
        )
        if response.status_code != 201:
            sys.exit(f"Enrollment failed: {response.status_code} {response.text[:300]}")

    print(f"Enrolled {args.name} (personId={response.json()['id']})")


if __name__ == "__main__":
    main()
