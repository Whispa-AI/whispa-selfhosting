"""AWS Connect Contact Flow Lambda for Whispa Integration.

This Lambda is invoked from an AWS Connect Contact Flow when a call starts.
It extracts call metadata and forwards it to the Whispa backend to start
audio capture and transcription.

Environment Variables:
    WHISPA_API_URL: Base URL for the Whispa backend (required)
    WHISPA_API_KEY: API key for authentication (required in production)

Contact Flow Setup:
    1. Add "Start media streaming" block FIRST
    2. Add "Invoke AWS Lambda function" block (select this Lambda)
    3. Continue with queue/transfer
"""

import json
import os
import urllib.request
import urllib.error

WHISPA_API_URL = os.environ.get("WHISPA_API_URL", "")
WHISPA_API_KEY = os.environ.get("WHISPA_API_KEY", "")


def lambda_handler(event, context):
    """Handle AWS Connect contact flow invocation.

    Extracts call metadata from the ContactData and forwards it to Whispa.

    Args:
        event: AWS Connect contact flow event containing ContactData
        context: Lambda context (unused)

    Returns:
        dict: Response with status, contactId, and streamArn for contact flow
    """
    print(f"Event received: {json.dumps(event)}")

    # Extract contact data from the event
    details = event.get("Details", {})
    contact_data = details.get("ContactData", {})

    # Core identifiers
    contact_id = contact_data.get("ContactId", "")
    instance_arn = contact_data.get("InstanceARN", "")

    # Customer info
    customer_endpoint = contact_data.get("CustomerEndpoint", {})
    customer_number = customer_endpoint.get("Address", "")

    # Agent info - check multiple sources in order of preference:
    # 1. ContactData.Agent (available in some flow contexts)
    # 2. ContactData.Attributes (set via "Set contact attributes" block)
    # 3. ContactData.Name (often contains agent username for outbound calls)
    agent_data = contact_data.get("Agent", {})
    attributes = contact_data.get("Attributes", {})

    # Try Agent object first, then Attributes, then Name field
    # Use None-based logic for clearer semantics (None = not present vs "" = empty value)
    agent_arn = agent_data.get("ARN") or attributes.get("agent_arn") or None
    agent_username = (
        agent_data.get("Username")
        or attributes.get("agent_username")
        or contact_data.get("Name")  # Fallback: Name field often has agent username
        or None
    )

    # Queue info
    queue_data = contact_data.get("Queue", {})
    queue_name = queue_data.get("Name", "")

    # Call direction/initiation method (INBOUND, OUTBOUND, TRANSFER, CALLBACK, API, etc.)
    initiation_method = contact_data.get("InitiationMethod", "")

    # Media stream info (from "Start media streaming" block)
    media_streams = contact_data.get("MediaStreams", {})
    customer_audio = media_streams.get("Customer", {}).get("Audio", {})
    stream_arn = customer_audio.get("StreamARN", "")

    # Validate required fields
    if not stream_arn:
        print(
            "ERROR: No StreamARN found. Ensure 'Start media streaming' block runs before this Lambda."
        )
        return {
            "statusCode": 400,
            "error": "Missing stream_arn - 'Start media streaming' block must run first",
        }

    if not contact_id:
        print("ERROR: No ContactId found in event.")
        return {"statusCode": 400, "error": "Missing contact_id in event"}

    # Build payload for Whispa
    payload = {
        "contact_id": contact_id,
        "stream_arn": stream_arn,
        "customer_number": customer_number or None,
        "agent_arn": agent_arn or None,
        "agent_username": agent_username or None,
        "instance_arn": instance_arn or None,
        "queue_name": queue_name or None,
        "initiation_method": initiation_method or None,
    }

    print(f"Payload for Whispa: {json.dumps(payload)}")

    # Forward to Whispa if configured
    if WHISPA_API_URL:
        endpoint = f"{WHISPA_API_URL.rstrip('/')}/awsconnect/call-started"
        headers = {"Content-Type": "application/json"}

        if WHISPA_API_KEY:
            headers["X-API-Key"] = WHISPA_API_KEY

        try:
            req = urllib.request.Request(
                endpoint,
                data=json.dumps(payload).encode("utf-8"),
                headers=headers,
                method="POST",
            )
            with urllib.request.urlopen(req, timeout=10) as resp:
                response_body = resp.read().decode("utf-8")
                print(f"Whispa response: status={resp.status}, body={response_body}")

        except urllib.error.HTTPError as e:
            error_body = e.read().decode("utf-8") if e.fp else ""
            print(f"Whispa HTTP error: status={e.code}, body={error_body}")
            # Don't fail the contact flow - just log the error

        except urllib.error.URLError as e:
            print(f"Whispa connection error: {e.reason}")
            # Don't fail the contact flow - just log the error

        except Exception as e:
            print(f"Whispa unexpected error: {type(e).__name__}: {e}")
            # Don't fail the contact flow - just log the error
    else:
        print("WARNING: WHISPA_API_URL not configured, call will not be captured")

    # Return success to contact flow
    # These values can be referenced in the contact flow if needed
    return {
        "statusCode": 200,
        "contactId": contact_id,
        "streamArn": stream_arn,
    }
