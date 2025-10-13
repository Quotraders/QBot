# RL Training Data Directory

## Purpose
This directory contains training data for reinforcement learning models.

## Storage Recommendation
For production deployments, large training data files (>1MB) should be stored in external storage systems such as:
- **AWS S3**
- **Azure Blob Storage**
- **Google Cloud Storage**

## Files in This Directory
- Small sample files for testing (< 1MB)
- Configuration files
- Training metadata

## What NOT to Commit
Large training data files are excluded via `.gitignore`:
- `*_training_*.jsonl` files
- `*_training_*.csv` files

These should be downloaded from external storage or generated locally as needed.

## Local Development
For local development and testing, you can:
1. Download training data from your cloud storage
2. Generate training data using the provided scripts
3. Use small sample files for testing

## Production Deployment
In production, configure your environment to:
1. Pull training data from external storage on startup
2. Store new training data to external storage
3. Use data versioning for model reproducibility
